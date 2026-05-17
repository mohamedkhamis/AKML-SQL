using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Transports;

namespace AkmlSql.Engine.Handlers.Diagnostics
{
    /// <summary>
    /// Spec 021 (web edition) -- diagnostics export extension. Returns the tail of
    /// the engine's daily rolling log file so the browser can append it to its
    /// diagnostics ZIP. Capability-gated on
    /// <c>diagnostics.engine-log-tail.v1</c>.
    ///
    /// <para>
    /// Hard caps: 1 MB regardless of what the request asks for; reads from the
    /// last byte backwards. Reads only the most-recent log file (the engine uses
    /// daily rolling files named <c>akmlsql-YYYYMMDD.log</c>).
    /// </para>
    /// </summary>
    public sealed class EngineLogTailHandler : IRpcRequestHandler<EngineLogTailRequest, EngineLogTailResponse>
    {
        private const int HardCapBytes = 1 * 1024 * 1024;   // 1 MB

        private readonly Func<string> _logDirResolver;

        public EngineLogTailHandler() : this(() => Constants.LogsPath) { }

        /// <summary>Test seam: substitute the log directory.</summary>
        internal EngineLogTailHandler(Func<string> logDirResolver)
        {
            _logDirResolver = logDirResolver ?? throw new ArgumentNullException(nameof(logDirResolver));
        }

        public int RequestMessageType => MessageTypes.EngineLogTailRequest;
        public int ResponseMessageType => MessageTypes.EngineLogTailResponse;

        public Task<EngineLogTailResponse> HandleAsync(
            EngineLogTailRequest request, RpcContext ctx, CancellationToken ct)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            var response = new EngineLogTailResponse();
            var maxBytes = Math.Max(1024, Math.Min(request.MaxBytes, HardCapBytes));

            string logDir;
            try { logDir = _logDirResolver(); }
            catch (Exception ex)
            {
                response.LogText = $"[engine] Failed to resolve log directory: {ex.Message}";
                return Task.FromResult(response);
            }

            if (!Directory.Exists(logDir))
            {
                response.LogText = "[engine] Log directory does not exist (no logs have been written yet).";
                return Task.FromResult(response);
            }

            // Pick the most-recent akmlsql-*.log file by LastWriteTimeUtc.
            FileInfo? latest;
            try
            {
                latest = new DirectoryInfo(logDir)
                    .GetFiles("akmlsql-*.log")
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .FirstOrDefault();
            }
            catch (Exception ex)
            {
                response.LogText = $"[engine] Failed to enumerate log directory: {ex.Message}";
                return Task.FromResult(response);
            }

            if (latest == null)
            {
                response.LogText = "[engine] No akmlsql-*.log files found.";
                return Task.FromResult(response);
            }

            response.SourcePath = latest.FullName;
            try
            {
                using var fs = new FileStream(latest.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (fs.Length > maxBytes)
                {
                    fs.Seek(-maxBytes, SeekOrigin.End);
                    response.Truncated = true;
                }
                using var reader = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                // If we seeked into the middle of a line, drop the partial first line so
                // the export ZIP always starts at a line boundary.
                if (response.Truncated)
                {
                    reader.ReadLine();
                }
                response.LogText = reader.ReadToEnd();
            }
            catch (Exception ex)
            {
                response.LogText = $"[engine] Failed to read log file: {ex.Message}";
            }

            return Task.FromResult(response);
        }
    }
}
