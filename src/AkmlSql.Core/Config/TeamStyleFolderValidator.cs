using System;
using System.IO;

namespace AkmlSql.Core.Config
{
    /// <summary>
    /// Spec 040 (STY-10, data-model 1.2) — checks the team style folder typed on Options › Format ›
    /// Styles. It must be a full local or UNC path; the value kept is its canonical
    /// <see cref="Path.GetFullPath(string)"/> form (Constitution security: path checks use the
    /// canonical form). A folder that can't be reached is allowed — the style list reports it.
    /// </summary>
    public static class TeamStyleFolderValidator
    {
        public const string RelativePathError =
            "Enter a full folder path, such as C:\\Styles or \\\\server\\share\\Styles.";

        public const string InvalidPathError = "That isn't a valid folder path.";

        /// <summary>
        /// Empty or whitespace → OK with no folder (team styles off). A full path → OK with its
        /// canonical form. Anything else → not OK, with the reason to show.
        /// </summary>
        public static (bool Ok, string? FullPath, string? Error) Normalize(string? input)
        {
            var text = (input ?? string.Empty).Trim();
            if (text.Length >= 2 && text[0] == '"' && text[text.Length - 1] == '"')
                text = text.Substring(1, text.Length - 2).Trim();
            if (text.Length == 0) return (true, null, null);

            if (text.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                return (false, null, InvalidPathError);

            if (!IsFullyQualified(text)) return (false, null, RelativePathError);

            try
            {
                return (true, Path.GetFullPath(text), null);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException
                                       || ex is PathTooLongException || ex is System.Security.SecurityException)
            {
                return (false, null, InvalidPathError);
            }
        }

        /// <summary>
        /// A drive path with a separator after the colon (<c>C:\x</c>) or a UNC path
        /// (<c>\\server\share</c>). <see cref="Path.IsPathRooted"/> alone also accepts
        /// <c>C:x</c> and <c>\x</c>, which resolve against whatever the current folder happens to be.
        /// (netstandard2.0 has no <c>Path.IsPathFullyQualified</c>.)
        /// </summary>
        private static bool IsFullyQualified(string path)
        {
            if (path.Length >= 3 && IsDriveLetter(path[0]) && path[1] == ':' && IsSeparator(path[2]))
                return true;
            return path.Length >= 3 && IsSeparator(path[0]) && IsSeparator(path[1]) && !IsSeparator(path[2]);
        }

        private static bool IsDriveLetter(char c) => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');

        private static bool IsSeparator(char c) => c == '\\' || c == '/';
    }
}
