using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;

namespace AkmlSql.PostgreSql.Tests.Completion;

/// <summary>
/// Spec 042 (task T095) - the fake schema cache the PostgreSQL completion corpus runs against, so the gate needs no server: the
/// lower-case Northwind tables in <c>public</c> (PostgreSQL port names: <c>customer_id</c>, <c>company_name</c>), a <c>sales</c>
/// schema, a view, a materialized view, a sequence, a table whose name needs double quotes, and a few functions and procedures.
/// </summary>
public static class NorthwindPgTestCacheFactory
{
    private sealed record Col(string Name, string Type, bool Pk = false, bool NotNull = false);

    public static DatabaseCache Create(params string[] searchPath)
    {
        var cache = new DatabaseCache
        {
            CacheKey = "pg|test:northwind",
            Kind = AkmlSql.Core.DatabaseKind.PostgreSql,
            Phase = PopulationPhase.Complete,
            SearchPath = searchPath.Length > 0 ? searchPath.ToList() : new List<string> { "public" },
            HiddenSchemas = new List<string> { "pg_catalog", "information_schema" },
        };

        var id = 1000;

        Table(cache, ref id, "public", "customers", new Col("customer_id", "text", true, true), new Col("company_name", "text", false, true), new Col("contact_name", "text"),
            new Col("contact_title", "text"), new Col("address", "text"), new Col("city", "text"), new Col("region", "text"), new Col("postal_code", "text"),
            new Col("country", "text"), new Col("phone", "text"), new Col("fax", "text"));

        Table(cache, ref id, "public", "orders", new Col("order_id", "integer", true, true), new Col("customer_id", "text"), new Col("employee_id", "integer"),
            new Col("order_date", "date"), new Col("required_date", "date"), new Col("shipped_date", "date"), new Col("ship_via", "integer"),
            new Col("freight", "numeric(10,2)"), new Col("ship_name", "text"), new Col("ship_address", "text"), new Col("ship_city", "text"),
            new Col("ship_region", "text"), new Col("ship_postal_code", "text"), new Col("ship_country", "text"));

        Table(cache, ref id, "public", "order_details", new Col("order_id", "integer", true, true), new Col("product_id", "integer", true, true),
            new Col("unit_price", "numeric(10,2)", false, true), new Col("quantity", "smallint", false, true), new Col("discount", "real", false, true));

        Table(cache, ref id, "public", "products", new Col("product_id", "integer", true, true), new Col("product_name", "text", false, true), new Col("supplier_id", "integer"),
            new Col("category_id", "integer"), new Col("quantity_per_unit", "text"), new Col("unit_price", "numeric(10,2)"), new Col("units_in_stock", "smallint"),
            new Col("units_on_order", "smallint"), new Col("reorder_level", "smallint"), new Col("discontinued", "integer", false, true));

        Table(cache, ref id, "public", "categories", new Col("category_id", "integer", true, true), new Col("category_name", "text", false, true),
            new Col("description", "text"), new Col("picture", "bytea"));

        Table(cache, ref id, "public", "employees", new Col("employee_id", "integer", true, true), new Col("last_name", "text", false, true), new Col("first_name", "text", false, true),
            new Col("title", "text"), new Col("title_of_courtesy", "text"), new Col("birth_date", "date"), new Col("hire_date", "date"), new Col("address", "text"),
            new Col("city", "text"), new Col("region", "text"), new Col("postal_code", "text"), new Col("country", "text"), new Col("home_phone", "text"),
            new Col("extension", "text"), new Col("reports_to", "integer"));

        Table(cache, ref id, "public", "suppliers", new Col("supplier_id", "integer", true, true), new Col("company_name", "text", false, true), new Col("contact_name", "text"),
            new Col("contact_title", "text"), new Col("address", "text"), new Col("city", "text"), new Col("region", "text"), new Col("postal_code", "text"),
            new Col("country", "text"), new Col("phone", "text"), new Col("fax", "text"), new Col("homepage", "text"));

        Table(cache, ref id, "public", "shippers", new Col("shipper_id", "integer", true, true), new Col("company_name", "text", false, true), new Col("phone", "text"));

        Table(cache, ref id, "public", "region", new Col("region_id", "integer", true, true), new Col("region_description", "text", false, true));

        Table(cache, ref id, "public", "territories", new Col("territory_id", "text", true, true), new Col("territory_description", "text", false, true), new Col("region_id", "integer", false, true));

        Table(cache, ref id, "public", "employee_territories", new Col("employee_id", "integer", true, true), new Col("territory_id", "text", true, true));

        // a name that needs double quotes, in a mixed case with a space
        Table(cache, ref id, "public", "Audit Log", new Col("log_id", "bigint", true, true), new Col("Event Name", "text", false, true), new Col("logged_at", "timestamp with time zone", false, true));

        // the schema the search path does not include
        Table(cache, ref id, "sales", "orders", new Col("order_id", "integer", true, true), new Col("customer_id", "text"), new Col("total", "numeric(12,2)", false, true));
        Table(cache, ref id, "sales", "invoices", new Col("invoice_id", "integer", true, true), new Col("order_id", "integer", false, true), new Col("amount", "numeric(12,2)", false, true),
            new Col("paid", "boolean", false, true));

        Relation(cache, ref id, "public", "vw_customer_orders", DbObjectType.View, new Col("customer_id", "text"), new Col("company_name", "text"), new Col("order_id", "integer"), new Col("order_date", "date"));
        Relation(cache, ref id, "public", "mv_product_sales", DbObjectType.MaterializedView, new Col("product_id", "integer"), new Col("product_name", "text"), new Col("total_quantity", "bigint"));
        Relation(cache, ref id, "public", "orders_order_id_seq", DbObjectType.Sequence, new Col("last_value", "bigint"), new Col("is_called", "boolean"));

        Function(cache, ref id, "public", "order_total", DbObjectType.ScalarFunction, ("order_id", "integer"));
        Function(cache, ref id, "public", "orders_by_customer", DbObjectType.TableFunction, ("customer", "text"));
        Function(cache, ref id, "public", "archive_orders", DbObjectType.Procedure, ("cutoff", "date"));
        Function(cache, ref id, "sales", "mark_invoice_paid", DbObjectType.Procedure, ("invoice_id", "integer"));

        cache.ForeignKeys = new List<ForeignKey>
        {
            Fk("orders_customer_id_fkey", "orders", "customer_id", "customers", "customer_id"),
            Fk("orders_employee_id_fkey", "orders", "employee_id", "employees", "employee_id"),
            Fk("orders_ship_via_fkey", "orders", "ship_via", "shippers", "shipper_id"),
            Fk("order_details_order_id_fkey", "order_details", "order_id", "orders", "order_id"),
            Fk("order_details_product_id_fkey", "order_details", "product_id", "products", "product_id"),
            Fk("products_supplier_id_fkey", "products", "supplier_id", "suppliers", "supplier_id"),
            Fk("products_category_id_fkey", "products", "category_id", "categories", "category_id"),
            Fk("employees_reports_to_fkey", "employees", "reports_to", "employees", "employee_id"),
            Fk("territories_region_id_fkey", "territories", "region_id", "region", "region_id"),
            Fk("employee_territories_employee_id_fkey", "employee_territories", "employee_id", "employees", "employee_id"),
            Fk("employee_territories_territory_id_fkey", "employee_territories", "territory_id", "territories", "territory_id"),
            new ForeignKey
            {
                FkName = "invoices_order_id_fkey",
                ParentSchema = "sales", ParentTable = "invoices", ParentColumns = new List<string> { "order_id" },
                ReferencedSchema = "sales", ReferencedTable = "orders", ReferencedColumns = new List<string> { "order_id" },
            },
        };
        cache.RebuildFkIndex();
        return cache;
    }

    private static ForeignKey Fk(string name, string parent, string parentColumn, string referenced, string referencedColumn) => new()
    {
        FkName = name,
        ParentSchema = "public", ParentTable = parent, ParentColumns = new List<string> { parentColumn },
        ReferencedSchema = "public", ReferencedTable = referenced, ReferencedColumns = new List<string> { referencedColumn },
    };

    private static void Table(DatabaseCache cache, ref int id, string schema, string name, params Col[] columns) =>
        Relation(cache, ref id, schema, name, DbObjectType.Table, columns);

    private static void Relation(DatabaseCache cache, ref int id, string schema, string name, DbObjectType type, params Col[] columns)
    {
        var entry = cache.Schemas.GetOrAdd(schema, s => new SchemaEntry { SchemaName = s });
        var o = new DatabaseObject { ObjectId = id++, SchemaName = schema, ObjectName = name, ObjectType = type, ColumnsLoaded = true };
        var n = 1;
        foreach (var c in columns)
            o.Columns.Add(new Column { ColumnId = n++, ColumnName = c.Name, TypeName = c.Type, IsPrimaryKey = c.Pk, IsNullable = !(c.NotNull || c.Pk) });
        entry.Objects.Add(o);
    }

    private static void Function(DatabaseCache cache, ref int id, string schema, string name, DbObjectType type, params (string Name, string Type)[] parameters)
    {
        var entry = cache.Schemas.GetOrAdd(schema, s => new SchemaEntry { SchemaName = s });
        var o = new DatabaseObject { ObjectId = id++, SchemaName = schema, ObjectName = name, ObjectType = type, ColumnsLoaded = true };
        var n = 1;
        foreach (var p in parameters)
            o.Parameters.Add(new Parameter { ParameterId = n++, ParameterName = p.Name, TypeName = p.Type });
        entry.Objects.Add(o);
    }
}
