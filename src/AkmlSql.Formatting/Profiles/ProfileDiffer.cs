namespace AkmlSql.Formatting.Profiles;

/// <summary>
/// Describes a single option that differs between two profiles.
/// </summary>
public class ProfileDifference
{
    public string Category { get; set; } = string.Empty;
    public string OptionName { get; set; } = string.Empty;
    public string OldValue { get; set; } = string.Empty;
    public string NewValue { get; set; } = string.Empty;
}

/// <summary>
/// Compares two <see cref="FormattingProfile"/> objects and returns a list of differing options.
/// Uses reflection to walk every property in each option category.
/// </summary>
public class ProfileDiffer
{
    /// <summary>
    /// Compares all option categories between <paramref name="left"/> and <paramref name="right"/>.
    /// Metadata is excluded from comparison.
    /// </summary>
    public List<ProfileDifference> Compare(FormattingProfile left, FormattingProfile right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var diffs = new List<ProfileDifference>();

        CompareCategory(diffs, "whitespace", left.Whitespace, right.Whitespace);
        CompareCategory(diffs, "casing", left.Casing, right.Casing);
        CompareCategory(diffs, "list", left.List, right.List);
        CompareCategory(diffs, "parenthesis", left.Parenthesis, right.Parenthesis);
        CompareCategory(diffs, "dml", left.Dml, right.Dml);
        CompareCategory(diffs, "join", left.Join, right.Join);
        CompareCategory(diffs, "ddl", left.Ddl, right.Ddl);
        CompareCategory(diffs, "controlFlow", left.ControlFlow, right.ControlFlow);
        CompareCategory(diffs, "case", left.Case, right.Case);
        CompareCategory(diffs, "cte", left.Cte, right.Cte);
        CompareCategory(diffs, "expression", left.Expression, right.Expression);
        CompareCategory(diffs, "formatActions", left.FormatActions, right.FormatActions);

        return diffs;
    }

    private static void CompareCategory<T>(List<ProfileDifference> diffs, string category, T left, T right)
    {
        if (left == null && right == null) return;
        if (left == null || right == null)
        {
            diffs.Add(new ProfileDifference
            {
                Category = category,
                OptionName = "(entire category)",
                OldValue = left == null ? "(null)" : "(present)",
                NewValue = right == null ? "(null)" : "(present)"
            });
            return;
        }

        foreach (var prop in typeof(T).GetProperties())
        {
            var leftVal = prop.GetValue(left)?.ToString() ?? "";
            var rightVal = prop.GetValue(right)?.ToString() ?? "";
            if (!string.Equals(leftVal, rightVal, StringComparison.Ordinal))
            {
                diffs.Add(new ProfileDifference
                {
                    Category = category,
                    OptionName = prop.Name,
                    OldValue = leftVal,
                    NewValue = rightVal
                });
            }
        }
    }
}
