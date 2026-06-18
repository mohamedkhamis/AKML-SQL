using AkmlSql.Formatting.Layout;
using AkmlSql.Formatting.Profiles;

namespace AkmlSql.Formatting.Rules;

/// <summary>
/// Spec 030 R1 — the ordered registry of layout <see cref="IRuleSet"/>s intended to run after
/// <c>LayoutEngine.BuildLayout</c> in <c>FormatterPipeline</c>. <see cref="DefaultOrder"/> is the
/// single source of apply-order; the order is load-bearing (rules mutate shared
/// <see cref="LayoutNode"/> break/indent state, so reordering is a correctness change, not cosmetic).
///
/// <para><b>Wired as the production default</b> (T008): <c>FormatterPipeline.LayoutRules</c> defaults
/// to <see cref="DefaultOrder"/>. The R1 rollout was staged behind the Stage-6 (semantic) and Stage-7
/// (idempotency) gates; the visual-indent regressions those gates do NOT catch (e.g. <c>DmlRules</c>
/// de-denting nested AND/OR/SET to column 0 inside subqueries / BEGIN-END) were fixed during the
/// per-group golden-oracle rework before enabling. <c>Parenthesis.RemoveRedundant</c> remains
/// force-disabled (non-idempotent). See <c>specs/030-sqlprompt-parity-closure/research.md</c> (R1).</para>
///
/// <para>Thread-safety: every rule class is stateless (only the public <c>Apply</c> method; all
/// helpers are <c>private static</c>; zero instance fields), so this shared static instance list is
/// safe to reuse across BulkFormatter's parallel threads, which mutate only the per-call node list.</para>
/// </summary>
public sealed class RuleEngine
{
    /// <summary>
    /// The target apply-order for the layout rule sets. Casing/Whitespace rule sets are deliberately
    /// excluded (they overlap CasingEngine and LayoutEngine). This is the only order ever exercised
    /// with all six active together (R1 spike "ALL" group).
    /// </summary>
    public static readonly IReadOnlyList<IRuleSet> DefaultOrder = new IRuleSet[]
    {
        new DmlRules(),
        new JoinRules(),
        new ListRules(),
        new ParenthesisRules(),
        new DdlRules(),
        new ControlFlowRules(),
    };

    /// <summary>Applies <see cref="DefaultOrder"/> to the layout nodes in order.</summary>
    public void Apply(List<LayoutNode> nodes, FormattingProfile profile)
    {
        foreach (var ruleSet in DefaultOrder)
            ruleSet.Apply(nodes, profile);
    }
}
