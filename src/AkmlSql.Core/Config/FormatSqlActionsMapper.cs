using System;
using AkmlSql.Core.Ipc.Messages;

namespace AkmlSql.Core.Config
{
    /// <summary>
    /// Spec 040 (STY-11) — turns the saved Format SQL actions (<see cref="FormatSqlActions"/>, words
    /// in config.json) into the wire form the engine reads (<see cref="FormatSqlActionsDto"/>, codes).
    /// Unknown or missing words mean "as the style says" — the default, so a config written before
    /// the choice existed, or hand-edited, keeps the style's own semicolons and brackets.
    /// </summary>
    public static class FormatSqlActionsMapper
    {
        public static FormatSqlActionsDto ToDto(FormatSqlActions? actions)
        {
            var a = actions ?? new FormatSqlActions();
            return new FormatSqlActionsDto
            {
                ApplyLayout = a.ApplyLayout,
                ApplyCasing = a.ApplyCasing,
                Semicolons = SemicolonsCode(a.Semicolons),
                SquareBrackets = SquareBracketsCode(a.SquareBrackets),
                ExpandWildcards = a.ExpandWildcards,
                QualifyObjectNames = a.QualifyObjectNames,
            };
        }

        /// <summary>"insert" → 1, "remove" → 2, "leave" → 0, anything else → 3 (as the style says).</summary>
        public static int SemicolonsCode(string? value) =>
            Is(value, FormatSqlActions.Insert) ? FormatSqlActionsDto.Insert
            : Is(value, FormatSqlActions.Remove) ? FormatSqlActionsDto.Remove
            : Is(value, FormatSqlActions.Leave) ? FormatSqlActionsDto.Leave
            : FormatSqlActionsDto.UseStyle;

        /// <summary>"add" → 1, "remove" → 2, "leave" → 0, anything else → 3 (as the style says).</summary>
        public static int SquareBracketsCode(string? value) =>
            Is(value, FormatSqlActions.Add) ? FormatSqlActionsDto.Add
            : Is(value, FormatSqlActions.Remove) ? FormatSqlActionsDto.Remove
            : Is(value, FormatSqlActions.Leave) ? FormatSqlActionsDto.Leave
            : FormatSqlActionsDto.UseStyle;

        private static bool Is(string? value, string word) =>
            value != null && string.Equals(value.Trim(), word, StringComparison.OrdinalIgnoreCase);
    }
}
