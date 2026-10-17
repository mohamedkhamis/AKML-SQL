using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Core.Text;
using AkmlSql.Engine.Parser;

namespace AkmlSql.Engine.Execution
{
    /// <summary>
    /// What <see cref="ResultSetReader"/> reports result sets into: one byte budget for the whole reply and
    /// the decision whether a set is kept.
    /// </summary>
    public interface IResultSink
    {
        /// <summary>Bytes the reply may still grow by.</summary>
        long BudgetRemaining { get; }

        /// <summary>Charge <paramref name="bytes"/> to the budget.</summary>
        void Consume(long bytes);

        /// <summary>A new set began (columns and provenance known, no rows yet). Returns its index, or -1 when
        /// the set is beyond <see cref="ExecuteOutcome.MaxResultSets"/>: drain it and keep nothing.</summary>
        int BeginSet(ExecuteResultSet set);
    }

    /// <summary>
    /// Spec 041 (research R17–R19, R23) — collects everything one execute produces, batch by batch, and builds
    /// the reply. Pure (no SQL types), so the aggregation rules are tested without a database:
    /// <list type="bullet">
    /// <item>Status: Ok when no error message anywhere; Error when any batch reported one (the script still ran
    /// on); TimedOut / Cancelled when the script stopped for that reason. <c>ErrorMessage</c> = the first
    /// error's text.</item>
    /// <item>Messages in stream order, with the legacy <c>Messages[]</c> worded by <see cref="SqlMessageText"/>;
    /// a SELECT's row count belongs to its set (the first count after the set began), every other count is
    /// DML and sums into <c>TotalRowsAffected</c> (-1 when none); a truncated set's count reads
    /// "(N rows returned; more exist)".</item>
    /// <item>Caps: <see cref="MaxMessages"/> (then <c>MessagesOmitted</c> and one System line),
    /// <see cref="MaxResultSets"/>, <see cref="MaxBatchRepeat"/>, <see cref="MaxBatches"/>, and one byte budget
    /// for batches, sets and messages together (a message's text is charged twice: it travels in both message
    /// arrays). Errors are never lost to the caps that hide the rest: past them an error still sets the status
    /// and <c>ErrorMessage</c>, and up to <see cref="MaxOverflowErrors"/> of them are kept, paid for from
    /// <see cref="ErrorReserveBytes"/> on top of the budget, so the reply stays under the frame cap.</item>
    /// </list>
    /// </summary>
    public sealed class ExecuteOutcome : IResultSink
    {
        public const int MaxMessages = 10_000;
        public const int MaxResultSets = 1_000;
        public const int MaxBatchRepeat = 10_000;

        /// <summary>The most batches one script may have: each is described in the reply.</summary>
        public const int MaxBatches = 20_000;

        /// <summary>Errors kept beyond <see cref="MaxMessages"/> or the byte budget.</summary>
        public const int MaxOverflowErrors = 100;

        /// <summary>What the errors kept beyond the byte budget may take (the frame cap is 1 MB above the budget).</summary>
        public const long ErrorReserveBytes = 256L * 1024;

        /// <summary>The reply must serialize under the 16 MB frame cap: stop well below it.</summary>
        public const long DefaultByteBudget = 15L * 1024 * 1024;

        /// <summary>The engine's text for a script that has something other than a count after GO.</summary>
        public const string SeparatorErrorText = "A fatal scripting error occurred.\n" + TsqlParserService.GoSeparatorError;

        private const long MessageOverheadBytes = 48;   // the DTO's other keys
        private const long BatchBytes = 48;             // one ExecuteBatchDto, serialized

        /// <summary>A set past <see cref="MaxResultSets"/> is current: its row count is a SELECT's, not DML.</summary>
        private const int DroppedSet = -2;

        private readonly List<ExecuteMessageDto> _messages = new();
        private readonly List<ExecuteBatchDto> _batches = new();
        private readonly List<ExecuteResultSet> _sets = new();
        private readonly HashSet<ExecuteMessageDto> _serverTags = new(ReferenceEqualityComparer.Instance);
        private long _budget;
        private int _messagesOmitted;
        private int _currentBatch = -1;
        private int _setAwaitingCount = -1;
        private int _stoppedStatus = ExecuteStatus.Ok;
        private long _dmlRows = -1;
        private long _errorReserve = ErrorReserveBytes;
        private int _overflowErrors;
        private string? _firstErrorText;

        public ExecuteOutcome(long byteBudget = DefaultByteBudget)
        {
            _budget = Math.Max(0, byteBudget);
        }

        public long BudgetRemaining => _budget;

        /// <summary>The batch being run, or -1 before the first.</summary>
        public int CurrentBatch => _currentBatch;

        /// <summary>The sets kept so far.</summary>
        public IReadOnlyList<ExecuteResultSet> ResultSets => _sets;

        /// <summary>True once the script stopped (timeout, cancellation): nothing more runs.</summary>
        public bool Stopped => _stoppedStatus != ExecuteStatus.Ok;

        public void Consume(long bytes)
        {
            if (bytes <= 0) return;
            _budget = Math.Max(0, _budget - bytes);
        }

        /// <summary>The engine's text for a script with more than <see cref="MaxBatches"/> batches.</summary>
        public static string TooManyBatchesText(int batches) =>
            $"The script has {batches.ToString("N0", CultureInfo.InvariantCulture)} batches; one run can take at most {MaxBatches.ToString("N0", CultureInfo.InvariantCulture)}. Split it into smaller scripts.";

        /// <summary>The engine's text when a script reaches its time limit (<see cref="ExecuteLimits.ScriptTimeLimit"/>).</summary>
        public static string ScriptTimeLimitText(TimeSpan limit) =>
            $"The script was stopped after {FormatDuration(limit)}, the longest it may run (the timeout for each command it runs, at most {FormatDuration(TimeSpan.FromSeconds(ExecuteLimits.ScriptTimeLimitCeilingSeconds))} in all).";

        private static string FormatDuration(TimeSpan t)
        {
            if (t.TotalSeconds < 120) return $"{Math.Round(t.TotalSeconds).ToString(CultureInfo.InvariantCulture)} seconds";
            if (t.TotalMinutes < 120) return $"{Math.Round(t.TotalMinutes).ToString(CultureInfo.InvariantCulture)} minutes";
            return $"{Math.Round(t.TotalHours, 1).ToString(CultureInfo.InvariantCulture)} hours";
        }

        // ── Batches ────────────────────────────────────────────────────────────────────────────────

        /// <summary>Charge the reply's description of <paramref name="batchCount"/> batches up front, so the sets
        /// and messages that follow cannot take the room it needs.</summary>
        public void ReserveBatches(int batchCount) => Consume(Math.Max(0, batchCount) * BatchBytes);

        /// <summary>A batch starts running. Returns its index.</summary>
        public int BeginBatch(SqlBatchSpan span, int repeatCount)
        {
            _currentBatch = _batches.Count;
            _batches.Add(new ExecuteBatchDto
            {
                Index = _currentBatch,
                StartLine = span.StartLine,
                StartOffset = span.StartOffset,
                EndOffset = span.EndOffset,
                Status = ExecuteStatus.Ok,
                RepeatCount = Math.Max(1, repeatCount),
            });
            _setAwaitingCount = -1;
            return _currentBatch;
        }

        /// <summary>A <c>GO n</c> count, limited to <see cref="MaxBatchRepeat"/> (with a System line saying so);
        /// the current batch records the count it will run.</summary>
        public int ClampRepeat(int requested)
        {
            int count = Math.Max(1, requested);
            if (count > MaxBatchRepeat)
            {
                AddSystem($"GO {requested.ToString(CultureInfo.InvariantCulture)} was limited to {MaxBatchRepeat.ToString("N0", CultureInfo.InvariantCulture)} runs.");
                count = MaxBatchRepeat;
            }
            if (_currentBatch >= 0) _batches[_currentBatch].RepeatCount = count;
            return count;
        }

        /// <summary>SSMS's line before a batch that runs more than once.</summary>
        public void BeginLoop() => AddSystem("Beginning execution loop");

        /// <summary>SSMS's line after the loop; <paramref name="times"/> is how many runs completed.</summary>
        public void EndLoop(int times) =>
            AddSystem($"Batch execution completed {times.ToString(CultureInfo.InvariantCulture)} times.");

        public void EndBatch(long elapsedMs)
        {
            if (_currentBatch < 0) return;
            _batches[_currentBatch].ElapsedMs = Math.Max(0, elapsedMs);
            _setAwaitingCount = -1;
        }

        /// <summary>The script stopped in the current batch (TimedOut or Cancelled): nothing more runs.</summary>
        public void Stop(int status)
        {
            _stoppedStatus = status;
            if (_currentBatch >= 0) _batches[_currentBatch].Status = status;
        }

        /// <summary>The connection closed: the batches that never ran are listed as Skipped, with SSMS's line.</summary>
        public void SkipRemaining(IReadOnlyList<SqlBatchSpan> remaining)
        {
            if (remaining.Count == 0) return;
            foreach (var span in remaining)
            {
                _batches.Add(new ExecuteBatchDto
                {
                    Index = _batches.Count,
                    StartLine = span.StartLine,
                    StartOffset = span.StartOffset,
                    EndOffset = span.EndOffset,
                    Status = ExecuteStatus.Skipped,
                    RepeatCount = Math.Max(1, Math.Min(span.RepeatCount, MaxBatchRepeat)),
                });
            }
            AddSystem($"The remaining {remaining.Count.ToString(CultureInfo.InvariantCulture)} batch(es) did not run because the connection was closed.");
        }

        /// <summary>A timeout ends the script: the batches after it are Skipped (no line — the timeout says why).</summary>
        public void SkipAfterStop(IReadOnlyList<SqlBatchSpan> remaining)
        {
            foreach (var span in remaining)
            {
                _batches.Add(new ExecuteBatchDto
                {
                    Index = _batches.Count,
                    StartLine = span.StartLine,
                    StartOffset = span.StartOffset,
                    EndOffset = span.EndOffset,
                    Status = ExecuteStatus.Skipped,
                    RepeatCount = Math.Max(1, Math.Min(span.RepeatCount, MaxBatchRepeat)),
                });
            }
        }

        // ── Messages ───────────────────────────────────────────────────────────────────────────────

        /// <summary>A server message (an <c>SqlError</c>): Error when its severity is 11 or more, else Info.</summary>
        public void AddServerMessage(int number, int severity, int state, int line, string? procedure, string text)
        {
            var kind = severity >= 11 ? ExecuteMessageKind.Error : ExecuteMessageKind.Info;
            Add(new ExecuteMessageDto
            {
                Kind = kind,
                Text = text ?? string.Empty,
                Number = number,
                Severity = severity,
                State = state,
                Line = Math.Max(0, line),
                Procedure = string.IsNullOrEmpty(procedure) ? null : procedure,
            });
            if (kind == ExecuteMessageKind.Error && _currentBatch >= 0)
            {
                var batch = _batches[_currentBatch];
                batch.ErrorCount++;
                if (batch.Status == ExecuteStatus.Ok) batch.Status = ExecuteStatus.Error;
            }
        }

        /// <summary>A statement's row count. The first count after a set began is that SELECT's; every other
        /// count is DML.</summary>
        public void AddRowCount(long count)
        {
            if (count < 0) return;
            int set = _setAwaitingCount;
            _setAwaitingCount = -1;
            if (set == -1) _dmlRows = (_dmlRows < 0 ? 0 : _dmlRows) + count;
            if (set < 0) set = -1;
            Add(new ExecuteMessageDto
            {
                Kind = ExecuteMessageKind.RowCount,
                Text = SqlMessageText.RowCount(count),
                RowCount = count,
                ResultSetIndex = set,
            });
        }

        /// <summary>
        /// Spec 042 (FR-012): a PostgreSQL statement's command tag (<c>UPDATE 12</c>, <c>CREATE TABLE</c>, <c>SELECT 3</c>)
        /// as its row-count line, worded as the server words it. The count accounts like <see cref="AddRowCount"/>: the
        /// first line after a set began is that SELECT's, any other count is DML; <paramref name="rowCount"/> of -1 (a
        /// DDL tag) adds the line and counts nothing.
        /// </summary>
        public void AddCommandTag(string tag, long rowCount)
        {
            int set = _setAwaitingCount;
            _setAwaitingCount = -1;
            if (set == -1 && rowCount >= 0) _dmlRows = (_dmlRows < 0 ? 0 : _dmlRows) + rowCount;
            if (set < 0) set = -1;
            var message = new ExecuteMessageDto
            {
                Kind = ExecuteMessageKind.RowCount,
                Text = tag ?? string.Empty,
                RowCount = rowCount,
                ResultSetIndex = set,
            };
            // The server's tag already says how many rows the statement returned; the row-cap rewrite in Build() is SSMS's wording.
            _serverTags.Add(message);
            Add(message);
        }

        /// <summary>
        /// Spec 042 (FR-019): the current batch did not run (a psql meta-command or a client-side COPY), so its status is
        /// Skipped even though a message about it was recorded.
        /// </summary>
        public void SetBatchStatus(int status)
        {
            if (_currentBatch >= 0) _batches[_currentBatch].Status = status;
        }

        /// <summary>
        /// Spec 042 (FR-013, FR-014): a PostgreSQL notice, warning or error. <paramref name="kind"/> is Info for notices and
        /// warnings (their level is in the text) and Error for errors, which also mark the statement. The error position
        /// (<paramref name="line"/> within the script, <paramref name="column"/> within that line, both 1-based, 0 when
        /// unknown) is what click-to-line uses.
        /// </summary>
        public void AddPostgresMessage(int kind, string text, int severity, string? sqlState = null, string? detail = null,
            string? hint = null, int line = 0, int column = 0)
        {
            Add(new ExecuteMessageDto
            {
                Kind = kind,
                Text = text ?? string.Empty,
                Severity = severity,
                SqlState = string.IsNullOrEmpty(sqlState) ? null : sqlState,
                Detail = string.IsNullOrEmpty(detail) ? null : detail,
                Hint = string.IsNullOrEmpty(hint) ? null : hint,
                Line = Math.Max(0, line),
                Column = Math.Max(0, column),
            });
            if (kind == ExecuteMessageKind.Error && _currentBatch >= 0)
            {
                var batch = _batches[_currentBatch];
                batch.ErrorCount++;
                if (batch.Status == ExecuteStatus.Ok) batch.Status = ExecuteStatus.Error;
            }
        }

        /// <summary>A line the engine adds.</summary>
        public void AddSystem(string text) => Add(new ExecuteMessageDto { Kind = ExecuteMessageKind.System, Text = text ?? string.Empty });

        /// <summary>The script was refused before anything ran (a GO line held something other than a count).</summary>
        public void RefuseScript(string text)
        {
            Add(new ExecuteMessageDto { Kind = ExecuteMessageKind.Error, Text = text ?? string.Empty });
        }

        /// <summary>An error the engine raises itself (the script reached its time limit).</summary>
        public void AddEngineError(string text) =>
            Add(new ExecuteMessageDto { Kind = ExecuteMessageKind.Error, Text = text ?? string.Empty });

        private void Add(ExecuteMessageDto message)
        {
            message.BatchIndex = Math.Max(0, _currentBatch);
            bool error = message.Kind == ExecuteMessageKind.Error;
            if (error && _firstErrorText == null) _firstErrorText = message.Text;

            long bytes = Encoding.UTF8.GetByteCount(message.Text) * 2L + MessageOverheadBytes;
            if (_messages.Count < MaxMessages && bytes <= _budget)
            {
                Consume(bytes);
                _messages.Add(message);
                return;
            }
            // Past the caps an error is still shown (a bounded number of them, paid from the reserve).
            if (error && _overflowErrors < MaxOverflowErrors && bytes <= _errorReserve)
            {
                _overflowErrors++;
                _errorReserve -= bytes;
                _messages.Add(message);
                return;
            }
            _messagesOmitted++;
        }

        // ── Result sets ────────────────────────────────────────────────────────────────────────────

        public int BeginSet(ExecuteResultSet set)
        {
            set.BatchIndex = Math.Max(0, _currentBatch);
            if (_sets.Count >= MaxResultSets)
            {
                // Not kept, but its row count is still this SELECT's, never DML.
                _setAwaitingCount = DroppedSet;
                return -1;
            }
            _sets.Add(set);
            _setAwaitingCount = _sets.Count - 1;
            return _setAwaitingCount;
        }

        // ── The reply ──────────────────────────────────────────────────────────────────────────────

        public ExecuteQueryResult Build(long completedAtUnixMs)
        {
            var messages = new List<ExecuteMessageDto>(_messages);
            foreach (var m in messages)
            {
                // A count on a set the row cap cut reads as SSMS's "returned; more exist", with the rows kept.
                if (m.Kind == ExecuteMessageKind.RowCount && m.ResultSetIndex >= 0 && m.ResultSetIndex < _sets.Count
                    && _sets[m.ResultSetIndex].Truncated && !_serverTags.Contains(m))
                {
                    m.Text = SqlMessageText.RowCount(_sets[m.ResultSetIndex].Rows.Length, moreExist: true);
                }
            }
            if (_messagesOmitted > 0)
            {
                messages.Add(new ExecuteMessageDto
                {
                    Kind = ExecuteMessageKind.System,
                    BatchIndex = Math.Max(0, _currentBatch),
                    Text = $"{_messagesOmitted.ToString("N0", CultureInfo.InvariantCulture)} more message(s) were not returned (the limit is {MaxMessages.ToString("N0", CultureInfo.InvariantCulture)}).",
                });
            }

            // The first error, whether or not a cap kept it out of the message list.
            int status = _stoppedStatus != ExecuteStatus.Ok ? _stoppedStatus
                : _firstErrorText != null ? ExecuteStatus.Error
                : ExecuteStatus.Ok;

            return new ExecuteQueryResult
            {
                Status = status,
                ErrorMessage = _firstErrorText,
                ResultSets = _sets.ToArray(),
                Messages = messages.Select(SqlMessageText.Format).ToArray(),
                TotalRowsAffected = _dmlRows > int.MaxValue ? int.MaxValue : (int)_dmlRows,
                MessageDetails = messages.ToArray(),
                Batches = _batches.ToArray(),
                CompletedAtUnixMs = completedAtUnixMs,
                MessagesOmitted = _messagesOmitted,
            };
        }
    }
}
