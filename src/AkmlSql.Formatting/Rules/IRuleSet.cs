using AkmlSql.Formatting.Layout;
using AkmlSql.Formatting.Profiles;

namespace AkmlSql.Formatting.Rules;

public interface IRuleSet
{
    void Apply(List<LayoutNode> nodes, FormattingProfile profile);
}
