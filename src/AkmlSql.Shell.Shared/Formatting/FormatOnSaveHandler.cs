using System;
using Serilog;

namespace AkmlSql.Shell.Shared.Formatting
{
    /// <summary>
    /// Hooks into the document save pipeline to auto-format SQL files before they are written to disk.
    /// Designed to listen to IVsRunningDocumentTable.OnBeforeSave events.
    /// </summary>
    internal class FormatOnSaveHandler
    {
        private bool _enabled;

        /// <summary>
        /// Enables or disables format-on-save.
        /// </summary>
        public void SetEnabled(bool enabled)
        {
            _enabled = enabled;
        }

        /// <summary>
        /// Gets whether format-on-save is currently enabled.
        /// </summary>
        public bool IsEnabled => _enabled;

        /// <summary>
        /// Called before a document is saved. If enabled and the document is a SQL file,
        /// formats the entire document content.
        /// </summary>
        /// <param name="filePath">The path of the file being saved.</param>
        /// <param name="documentText">The current text of the document.</param>
        /// <param name="formattedText">The formatted output if formatting was applied.</param>
        /// <returns>True if the document was formatted; false if it was left unchanged.</returns>
        public bool TryFormatBeforeSave(string filePath, string documentText, out string formattedText)
        {
            formattedText = documentText;

            if (!_enabled || string.IsNullOrWhiteSpace(documentText))
                return false;

            try
            {
                if (!IsSqlFile(filePath))
                    return false;

                // TODO: Wire to formatter pipeline via engine IPC when available.
                // For now this is a stub — actual formatting will be routed through
                // PipeRpcClient to the out-of-process engine.
                Log.Debug("Format on save: SQL file detected ({FilePath}), formatting stub invoked", filePath);

                // Stub: return unmodified until engine IPC wiring is complete
                return false;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Format on save failed for {FilePath}", filePath);
            }

            return false;
        }

        /// <summary>
        /// Determines if the file extension indicates a SQL file.
        /// </summary>
        private static bool IsSqlFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return false;

            var ext = System.IO.Path.GetExtension(filePath);
            return string.Equals(ext, ".sql", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ext, ".tsql", StringComparison.OrdinalIgnoreCase);
        }
    }
}
