using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Handlers.Control;
using Xunit;

namespace AkmlSql.Engine.Tests
{
    public class TestSqlConnectionHandlerTests
    {
        [Fact]
        public async Task EmptyConnectionString_ReturnsNotOk()
        {
            var handler = new TestSqlConnectionHandler();
            var resp = await handler.HandleAsync(new TestSqlConnectionRequest { ConnectionString = "" }, null!, CancellationToken.None);
            Assert.False(resp.Ok);
            Assert.False(string.IsNullOrEmpty(resp.ErrorMessage));
        }

        [Fact]
        public async Task UnreachableServer_ReturnsNotOk_WithMessage()
        {
            var handler = new TestSqlConnectionHandler();
            var req = new TestSqlConnectionRequest
            {
                ConnectionString = "Data Source=akml-nonexistent-host-xyz,14330;Initial Catalog=x;" +
                                   "User ID=sa;Password=wrong;Connect Timeout=2;TrustServerCertificate=true;Encrypt=false"
            };
            var resp = await handler.HandleAsync(req, null!, CancellationToken.None);
            Assert.False(resp.Ok);
            Assert.False(string.IsNullOrEmpty(resp.ErrorMessage));
        }
    }
}
