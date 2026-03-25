#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace AkmlSql.Shell.Shared.Execution
{
    /// <summary>
    /// T079: Executes SQL against multiple databases in parallel using separate SqlConnections.
    /// Each database gets its own connection and the results are collected into <see cref="DatabaseExecutionResult"/>.
    /// </summary>
    internal sealed class MultiDatabaseExecutor
    {
        private readonly string _baseConnectionString;
        private readonly int _commandTimeoutSeconds;

        /// <summary>
        /// Initializes a new executor for the given server connection string.
        /// </summary>
        /// <param name="baseConnectionString">
        /// Connection string template. The <c>Initial Catalog</c> will be overridden per database.
        /// </param>
        /// <param name="commandTimeoutSeconds">SQL command timeout per database.</param>
        public MultiDatabaseExecutor(string baseConnectionString, int commandTimeoutSeconds = 30)
        {
            _baseConnectionString = baseConnectionString;
            _commandTimeoutSeconds = commandTimeoutSeconds;
        }

        /// <summary>
        /// Executes the given SQL against all specified databases in parallel.
        /// </summary>
        /// <param name="sql">The SQL text to execute.</param>
        /// <param name="databases">List of database names to execute against.</param>
        /// <param name="maxDegreeOfParallelism">Maximum concurrent database connections.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A result per database.</returns>
        public async Task<List<DatabaseExecutionResult>> ExecuteAsync(
            string sql,
            IReadOnlyList<string> databases,
            int maxDegreeOfParallelism = 4,
            CancellationToken cancellationToken = default)
        {
            Log.Information("MultiDatabaseExecutor: executing against {Count} databases (parallelism={P})",
                databases.Count, maxDegreeOfParallelism);

            var results = new ConcurrentBag<DatabaseExecutionResult>();
            var semaphore = new SemaphoreSlim(maxDegreeOfParallelism);

            var tasks = databases.Select(async db =>
            {
                await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var result = await ExecuteOnDatabaseAsync(sql, db, cancellationToken)
                        .ConfigureAwait(false);
                    results.Add(result);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks).ConfigureAwait(false);

            return results.OrderBy(r => r.DatabaseName).ToList();
        }

        private async Task<DatabaseExecutionResult> ExecuteOnDatabaseAsync(
            string sql, string databaseName, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var result = new DatabaseExecutionResult { DatabaseName = databaseName };

            try
            {
                var csb = new SqlConnectionStringBuilder(_baseConnectionString)
                {
                    InitialCatalog = databaseName
                };

                using var conn = new SqlConnection(csb.ConnectionString);
                await conn.OpenAsync(ct).ConfigureAwait(false);

                using var cmd = new SqlCommand(sql, conn)
                {
                    CommandTimeout = _commandTimeoutSeconds
                };

                using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

                // Read all result sets
                do
                {
                    var table = new DataTable();
                    table.Load(reader);
                    result.ResultSets.Add(table);
                    result.TotalRowsAffected += table.Rows.Count;
                } while (!reader.IsClosed && reader.NextResult());

                result.Success = true;
                sw.Stop();
                result.ElapsedMs = sw.ElapsedMilliseconds;

                Log.Debug("MultiDatabaseExecutor: {Database} completed in {Elapsed}ms, {Rows} rows",
                    databaseName, result.ElapsedMs, result.TotalRowsAffected);
            }
            catch (OperationCanceledException)
            {
                result.Success = false;
                result.ErrorMessage = "Execution cancelled";
                result.ElapsedMs = sw.ElapsedMilliseconds;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                result.ElapsedMs = sw.ElapsedMilliseconds;

                Log.Warning(ex, "MultiDatabaseExecutor: {Database} failed", databaseName);
            }

            return result;
        }
    }

    /// <summary>
    /// Holds the execution result for a single database in a multi-database execution.
    /// </summary>
    internal sealed class DatabaseExecutionResult
    {
        public string DatabaseName { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public long ElapsedMs { get; set; }
        public int TotalRowsAffected { get; set; }
        public List<DataTable> ResultSets { get; } = new List<DataTable>();
    }
}
