using AkmlSql.Core.Config;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Server;
using Serilog;
using Xunit;

namespace AkmlSql.Engine.Tests;

// Spec 022 (M0 closure) -- P1 / US1.
// Covers the new RpcContext settings-access surface (EnsureSettings / InvalidateSettings)
// per specs/022-m0-engine-closure/contracts/rpc-context-settings.md.
public class RpcContextTests
{
    private static RpcContext NewContext(Func<AppSettings> loader) => new()
    {
        Sessions = new SessionManager(),
        SchemaCache = new SchemaCacheManager(),
        Logger = Log.Logger,
        ParserService = new TsqlParserService(),
        SettingsLoader = loader,
    };

    [Fact]
    public void EnsureSettings_loads_once_and_caches()
    {
        int loadCount = 0;
        var ctx = NewContext(() => { loadCount++; return new AppSettings(); });

        var s1 = ctx.EnsureSettings();
        var s2 = ctx.EnsureSettings();

        Assert.Same(s1, s2);
        Assert.Equal(1, loadCount);
    }

    [Fact]
    public void InvalidateSettings_forces_reload_on_next_call()
    {
        int loadCount = 0;
        var ctx = NewContext(() => { loadCount++; return new AppSettings(); });

        var first = ctx.EnsureSettings();
        ctx.InvalidateSettings();
        var second = ctx.EnsureSettings();

        Assert.Equal(2, loadCount);
        Assert.NotSame(first, second);
    }
}
