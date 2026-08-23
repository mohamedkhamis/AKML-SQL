namespace AkmlSql.Formatting.Profiles;

/// <summary>
/// Spec 033 (T019) — declarative editor metadata for one <see cref="FormattingProfile"/> option
/// property. <see cref="FormatSettingSchema.BuildDefault"/> reads it via reflection to populate
/// the Format Styles editor schema (descriptions, enum dropdown values, int ranges).
///
/// <para>
/// Every settable option property MUST carry this attribute with a non-empty
/// <see cref="Description"/> — the schema completeness test fails listing all offenders
/// otherwise, so new options cannot ship undescribed.
/// </para>
///
/// <para>
/// <see cref="AllowedValues"/> entries are the EXACT stored spellings the profile JSON uses
/// (mixed case preserved: <c>"UPPERCASE"</c>, <c>"AsIs"</c>, <c>"trailing"</c>) and must
/// include the property's default — the editor persists the selected entry verbatim.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SettingMetaAttribute : Attribute
{
    /// <summary>Sentinel for "no range bound declared".</summary>
    public const int Unset = int.MinValue;

    /// <summary>One-sentence user-facing description shown with the setting's control.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// For enum-like string options: the complete set of accepted stored values, exact
    /// spelling, default included. Null for bool/int/free-string options.
    /// </summary>
    public string[]? AllowedValues { get; set; }

    /// <summary>Inclusive lower bound for int options (<see cref="Unset"/> = unbounded).</summary>
    public int Min { get; set; } = Unset;

    /// <summary>Inclusive upper bound for int options (<see cref="Unset"/> = unbounded).</summary>
    public int Max { get; set; } = Unset;
}
