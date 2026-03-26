using System.Collections.Generic;
using System.Threading;
using AkmlSql.Engine.Schema;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis;

public class AnalysisContext
{
    public TSqlScript Script { get; set; } = null!;
    public TSqlBatch CurrentBatch { get; set; } = null!;
    public IList<TSqlParserToken> Tokens { get; set; } = null!;
    public string DocumentText { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public DatabaseCache? SchemaCache { get; set; }
    public ResolvedAnalysisSettings Settings { get; set; } = new();
    public SuppressionMap Suppressions { get; set; } = SuppressionMap.Empty;
    public CancellationToken CancellationToken { get; set; }
}
