using System;
using AkmlSql.Core.Ipc.Messages;

namespace AkmlSql.Core.Config
{
    /// <summary>
    /// Spec 040 (STY-11) — turns the saved Format SQL actions (<see cref="FormatSqlActions"/>, words
    /// in config.json) into the wire form the engine reads (<see cref="FormatSqlActionsDto"/>, codes).
    /// Unknown or missing words mean "leave", so a hand-edited config can never ask for an action
    /// the user didn't choose.
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

        /// <summary>"insert" → 1, "remove" → 2, anything else → 0 (leave).</summary>
        public static int SemicolonsCode(string? value) =>
            Is(value, FormatSqlActions.Insert) ? FormatSqlActionsDto.Insert
            : Is(value, FormatSqlActions.Remove) ? FormatSqlActionsDto.Remove
            : FormatSqlActionsDto.Leave;

        /// <summary>"add" → 1, "remove" → 2, anything else → 0 (leave).</summary>
        public static int SquareBracketsCode(string? value) =>
            Is(value, FormatSqlActions.Add) ? FormatSqlActionsDto.Add
            : Is(value, FormatSqlActions.Remove) ? FormatSqlActionsDto.Remove
            : FormatSqlActionsDto.Leave;

        private static bool Is(string? value, string word) =>
            value != null && string.Equals(value.Trim(), word, StringComparison.OrdinalIgnoreCase);
    }
}
