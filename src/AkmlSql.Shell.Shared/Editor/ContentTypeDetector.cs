using System.Threading;
using Microsoft.VisualStudio.Text.Editor;
using Serilog;

namespace AkmlSql.Shell.Shared.Editor
{
    /// <summary>
    /// Discovers the SSMS T-SQL content type string at runtime.
    /// This is critical because SSMS may use different content type names
    /// than standard VS (e.g., "T-SQL" or "SQL Server Tools").
    /// </summary>
    internal static class ContentTypeDetector
    {
        private static volatile string _detectedContentType;
        private static int _detected;

        public static string GetContentType(ITextView textView)
        {
            if (Interlocked.CompareExchange(ref _detected, 0, 0) == 1)
                return _detectedContentType ?? "T-SQL";

            var contentType = textView?.TextBuffer?.ContentType?.TypeName;
            if (!string.IsNullOrEmpty(contentType))
            {
                Log.Information("Detected editor content type: {ContentType}", contentType);
                _detectedContentType = contentType;
                Interlocked.Exchange(ref _detected, 1);
            }

            return _detectedContentType ?? "T-SQL";
        }
    }
}
