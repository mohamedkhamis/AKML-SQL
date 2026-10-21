using Xunit;

namespace AkmlSql.Engine.Tests.PostgreSql;

/// <summary>
/// Spec 042 (task T126). The T-SQL parser's invocation count is process-wide, so a test that asserts it did not move must be the
/// only thing running. xUnit runs a collection that disables parallelization alone, after the parallel ones have finished, so no
/// other test can parse T-SQL while the count is being watched. Every test class that reads the count joins this collection.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TsqlParserCounterCollection
{
    public const string Name = "T-SQL parser invocation count";
}
