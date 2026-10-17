using AkmlSql.Core;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Navigation;
using AkmlSql.Engine.PostgreSql;
using AkmlSql.Engine.Schema;
using MessagePack;
using Xunit;

namespace AkmlSql.Engine.Tests.PostgreSql;

/// <summary>
/// Spec 042 (task T084, FR-068): go to definition for PostgreSQL objects. Views and functions give the server's own text; a table
/// is rebuilt from the loaded catalog; a name resolves through the search path; an unknown name is not found.
/// </summary>
[Collection("PostgreSqlLive")]
[Trait("Category", "PostgreSql")]
public class PgObjectDefinitionTests
{
    private const string Seed = @"
CREATE SCHEMA sales;
CREATE TABLE sales.customers (id serial PRIMARY KEY, name varchar(50) NOT NULL, created timestamptz DEFAULT now());
CREATE TABLE sales.orders (id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY, customer_id int NOT NULL REFERENCES sales.customers(id) ON DELETE CASCADE, total numeric(12,2));
CREATE INDEX orders_customer ON sales.orders (customer_id DESC);
CREATE VIEW sales.big_orders AS SELECT id, total FROM sales.orders WHERE total > 100;
CREATE MATERIALIZED VIEW sales.order_totals AS SELECT customer_id, sum(total) AS t FROM sales.orders GROUP BY customer_id;
CREATE FUNCTION sales.add_numbers(a int, b int DEFAULT 1) RETURNS int LANGUAGE sql AS 'SELECT a + b';
CREATE PROCEDURE sales.archive(IN cutoff date) LANGUAGE sql AS 'SELECT 1';
CREATE TABLE public.plain (x int);
CREATE VIEW public.plain_view AS SELECT x FROM public.plain;
";

    private static async Task<(DatabaseCache Cache, PgTestDatabase Database)> LoadAsync()
    {
        var database = await PgTestServer.CreateDatabaseAsync(Seed);
        var service = new PgSchemaMetadataService();
        var cache = new DatabaseCache { CacheKey = "defs", Kind = DatabaseKind.PostgreSql };
        await service.PopulatePhaseAAsync(cache, database.ConnectionString, CancellationToken.None);
        await service.PopulatePhaseBAsync(cache, database.ConnectionString, CancellationToken.None);
        return (cache, database);
    }

    [SkippableFact]
    public async Task A_view_comes_from_pg_get_viewdef_under_its_create_statement()
    {
        var (cache, database) = await LoadAsync();
        await using var _ = database;

        var (definition, type, name) = await new PgObjectDefinitionService().GetDefinitionAsync("big_orders", "sales", database.ConnectionString, cache, default);

        Assert.StartsWith("CREATE VIEW sales.big_orders AS\n", definition);
        Assert.Contains("FROM sales.orders", definition);
        Assert.Contains("total > 100", definition);
        Assert.EndsWith(";\n", definition);
        Assert.Equal(("View", "sales.big_orders"), (type, name));
    }

    [SkippableFact]
    public async Task A_materialized_view_says_so()
    {
        var (cache, database) = await LoadAsync();
        await using var _ = database;

        var (definition, type, _) = await new PgObjectDefinitionService().GetDefinitionAsync("order_totals", "sales", database.ConnectionString, cache, default);

        Assert.StartsWith("CREATE MATERIALIZED VIEW sales.order_totals AS\n", definition);
        Assert.Equal("MaterializedView", type);
    }

    [SkippableFact]
    public async Task A_function_and_a_procedure_come_from_pg_get_functiondef()
    {
        var (cache, database) = await LoadAsync();
        await using var _ = database;
        var service = new PgObjectDefinitionService();

        var (function, functionType, functionName) = await service.GetDefinitionAsync("add_numbers", "sales", database.ConnectionString, cache, default);
        var (procedure, procedureType, _) = await service.GetDefinitionAsync("archive", "sales", database.ConnectionString, cache, default);

        Assert.StartsWith("CREATE OR REPLACE FUNCTION sales.add_numbers(", function);
        Assert.Contains("SELECT a + b", function);
        Assert.Equal(("Function", "sales.add_numbers"), (functionType, functionName));
        Assert.StartsWith("CREATE OR REPLACE PROCEDURE sales.archive(", procedure);
        Assert.Equal("Procedure", procedureType);
    }

    [SkippableFact]
    public async Task A_table_is_rebuilt_with_columns_keys_foreign_keys_and_indexes()
    {
        var (cache, database) = await LoadAsync();
        await using var _ = database;
        var service = new PgObjectDefinitionService();

        var (customers, type, name) = await service.GetDefinitionAsync("customers", "sales", database.ConnectionString, cache, default);
        var (orders, _, _) = await service.GetDefinitionAsync("orders", "sales", database.ConnectionString, cache, default);

        Assert.Equal(("Table", "sales.customers"), (type, name));
        Assert.Contains("CREATE TABLE sales.customers (", customers);
        // Names that need no quotes are written bare.
        Assert.Contains("id integer DEFAULT nextval(", customers);
        Assert.Contains("name character varying(50) NOT NULL", customers);
        Assert.Contains("PRIMARY KEY (id)", customers);

        Assert.Contains("GENERATED BY DEFAULT AS IDENTITY", orders);
        Assert.Contains("FOREIGN KEY (customer_id)", orders);
        Assert.Contains("REFERENCES sales.customers (id)", orders);
        Assert.Contains("ON DELETE CASCADE", orders);
        Assert.Contains("CREATE INDEX orders_customer", orders);
        Assert.Contains("(customer_id DESC)", orders);
    }

    [SkippableFact]
    public async Task An_unqualified_name_resolves_through_the_search_path()
    {
        var (cache, database) = await LoadAsync();
        await using var _ = database;
        var service = new PgObjectDefinitionService();

        var (view, viewType, viewName) = await service.GetDefinitionAsync("plain_view", null, database.ConnectionString, cache, default);
        var (table, tableType, _) = await service.GetDefinitionAsync("plain", null, database.ConnectionString, cache, default);

        Assert.Equal(("View", "public.plain_view"), (viewType, viewName));
        Assert.Contains("FROM plain", view);
        Assert.Equal("Table", tableType);
        Assert.Contains("CREATE TABLE public.plain (", table);   // the definition names the schema even when the search path would
    }

    [SkippableFact]
    public async Task An_unknown_object_is_not_found()
    {
        var (cache, database) = await LoadAsync();
        await using var _ = database;

        var (definition, type, name) = await new PgObjectDefinitionService().GetDefinitionAsync("nothing_here", null, database.ConnectionString, cache, default);

        Assert.Null(definition);
        Assert.Null(type);
        Assert.Null(name);
    }

    [SkippableFact]
    public async Task The_request_handler_uses_the_PostgreSQL_service_for_a_PostgreSQL_session()
    {
        var (cache, database) = await LoadAsync();
        await using var _ = database;
        var engine = EngineComposition.Build();
        const string session = "defs-session";
        engine.Context.Sessions.UpdateSession(new ConnectionInfo
        {
            SessionId = session, ConnectionString = database.ConnectionString, DatabaseName = database.Name, Kind = (int)DatabaseKind.PostgreSql,
        });
        var handler = new NavigationRequestHandler(engine.Context.SchemaCache, id => id == session ? DatabaseKind.PostgreSql : DatabaseKind.SqlServer);

        var reply = await handler.HandleGetObjectDefinitionAsync(
            new RpcMessage
            {
                MessageType = MessageTypes.GetObjectDefinition,
                RequestId = 1,
                Payload = MessagePackSerializer.Serialize(new GetObjectDefinitionRequest { SessionId = session, ObjectName = "big_orders", SchemaName = "sales" }),
            },
            _ => (database.ConnectionString, database.Name),
            default);

        var response = MessagePackSerializer.Deserialize<GetObjectDefinitionResponse>(reply!.Payload!);
        Assert.True(response.Success, response.Error);
        Assert.StartsWith("CREATE VIEW sales.big_orders AS", response.Definition);
        Assert.Equal("sales.big_orders", response.FullName);
    }
}
