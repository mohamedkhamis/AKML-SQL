using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;

namespace AkmlSql.Engine.Tests.Completion;

/// <summary>
/// Spec 032 (T002) — builds a <see cref="DatabaseCache"/> shaped like the campaign's live
/// <c>Northwind_AutoTest</c> sandbox (W3Schools Northwind variant + campaign enrichments), so the
/// corpus gate (<see cref="CorpusGateTests"/>) and per-cluster tests can run the 2026-07-16 corpus
/// engine-level without a SQL Server. Shape source: doc/web-autocomplete-campaign-2026-07-16.md
/// (Environment table) + the corpus expectations themselves. Keep faithful — corpus pass rates
/// depend on it.
/// </summary>
public static class NorthwindAutoTestCacheFactory
{
    public static DatabaseCache Create()
    {
        var cache = new DatabaseCache
        {
            CacheKey = "test:Northwind_AutoTest",
            Phase = PopulationPhase.PhaseB,
            LastFullRefresh = DateTime.UtcNow,
        };

        var dbo = new SchemaEntry { SchemaName = "dbo" };
        var sales = new SchemaEntry { SchemaName = "Sales" };
        cache.Schemas["dbo"] = dbo;
        cache.Schemas["Sales"] = sales;

        // ── Tables (W3Schools Northwind) ─────────────────────────────────────
        dbo.Objects.Add(Table("dbo", "Customers",
            Pk("CustomerID"),
            Col("CustomerName", "nvarchar", 100),
            Col("ContactName", "nvarchar", 100),
            Col("Address", "nvarchar", 200),
            Col("City", "nvarchar", 50),
            Col("PostalCode", "nvarchar", 20),
            Col("Country", "nvarchar", 50)));

        dbo.Objects.Add(Table("dbo", "Categories",
            Pk("CategoryID"),
            Col("CategoryName", "nvarchar", 100),
            Col("Description", "nvarchar", 400)));

        dbo.Objects.Add(Table("dbo", "Employees",
            Pk("EmployeeID"),
            Col("LastName", "nvarchar", 50),
            Col("FirstName", "nvarchar", 50),
            Col("BirthDate", "datetime"),
            Col("Photo", "nvarchar", 200),
            Col("Notes", "nvarchar", -1)));

        dbo.Objects.Add(Table("dbo", "Shippers",
            Pk("ShipperID"),
            Col("ShipperName", "nvarchar", 100),
            Col("Phone", "nvarchar", 30)));

        dbo.Objects.Add(Table("dbo", "Suppliers",
            Pk("SupplierID"),
            Col("SupplierName", "nvarchar", 100),
            Col("ContactName", "nvarchar", 100),
            Col("Address", "nvarchar", 200),
            Col("City", "nvarchar", 50),
            Col("PostalCode", "nvarchar", 20),
            Col("Country", "nvarchar", 50),
            Col("Phone", "nvarchar", 30)));

        dbo.Objects.Add(Table("dbo", "Products",
            Pk("ProductID"),
            Col("ProductName", "nvarchar", 100),
            Col("SupplierID", "int"),
            Col("CategoryID", "int"),
            Col("Unit", "nvarchar", 50),
            Col("Price", "decimal", precision: 10, scale: 2)));

        dbo.Objects.Add(Table("dbo", "Orders",
            Pk("OrderID"),
            Col("CustomerID", "int"),
            Col("EmployeeID", "int"),
            Col("OrderDate", "datetime"),
            Col("ShipperID", "int")));

        dbo.Objects.Add(Table("dbo", "OrderDetails",
            Pk("OrderDetailID"),
            Col("OrderID", "int"),
            Col("ProductID", "int"),
            Col("Quantity", "int")));

        // Campaign enrichment: Sales schema. NO CustomerID column — the corpus bans it
        // (SUBQ-010), so the real sandbox's Invoices didn't have one.
        sales.Objects.Add(Table("Sales", "Invoices",
            Pk("InvoiceID"),
            Col("OrderID", "int"),
            Col("InvoiceDate", "datetime"),
            Col("TotalAmount", "decimal", precision: 12, scale: 2),
            Col("Paid", "bit")));

        // ── Views ────────────────────────────────────────────────────────────
        dbo.Objects.Add(Obj("dbo", "vw_CustomerOrders", DbObjectType.View,
            Col("CustomerID", "int"),
            Col("CustomerName", "nvarchar", 100),
            Col("Country", "nvarchar", 50),
            Col("OrderID", "int"),
            Col("OrderDate", "datetime"),
            Col("ShipperID", "int")));

        dbo.Objects.Add(Obj("dbo", "vw_ProductCatalog", DbObjectType.View,
            Col("ProductID", "int"),
            Col("ProductName", "nvarchar", 100),
            Col("Price", "decimal", precision: 10, scale: 2),
            Col("CategoryName", "nvarchar", 100),
            Col("SupplierName", "nvarchar", 100)));

        // ── Stored procedures (Phase-B parameters loaded) ────────────────────
        dbo.Objects.Add(Proc("dbo", "usp_GetCustomerOrders",
            Param(1, "@CustomerID", "int"),
            Param(2, "@FromDate", "datetime"),
            Param(3, "@ToDate", "datetime")));

        dbo.Objects.Add(Proc("dbo", "usp_UpdateProductPrice",
            Param(1, "@ProductID", "int"),
            Param(2, "@NewPrice", "decimal")));

        sales.Objects.Add(Proc("Sales", "usp_MarkInvoicePaid",
            Param(1, "@InvoiceID", "int")));

        // Database-diagram procs present in the real sandbox (corpus EXEC-002/012/029 expect them).
        dbo.Objects.Add(Proc("dbo", "sp_helpdiagrams",
            Param(1, "@diagramname", "sysname"),
            Param(2, "@owner_id", "int")));
        dbo.Objects.Add(Proc("dbo", "sp_creatediagram",
            Param(1, "@diagramname", "sysname"),
            Param(2, "@owner_id", "int"),
            Param(3, "@version", "int"),
            Param(4, "@definition", "varbinary")));

        // ── Functions ────────────────────────────────────────────────────────
        var fnScalar = Obj("dbo", "fn_OrderItemCount", DbObjectType.ScalarFunction);
        fnScalar.Parameters.Add(Param(1, "@OrderID", "int"));
        dbo.Objects.Add(fnScalar);

        var fnTvf = Obj("dbo", "fn_OrdersByCustomer", DbObjectType.TableFunction,
            Col("OrderID", "int"),
            Col("OrderDate", "datetime"),
            Col("ShipperID", "int"));
        fnTvf.Parameters.Add(Param(1, "@CustomerID", "int"));
        dbo.Objects.Add(fnTvf);

        // ── Foreign keys ─────────────────────────────────────────────────────
        cache.ForeignKeys.AddRange(
        [
            Fk("FK_Orders_Customers", "dbo", "Orders", "CustomerID", "dbo", "Customers", "CustomerID"),
            Fk("FK_Orders_Employees", "dbo", "Orders", "EmployeeID", "dbo", "Employees", "EmployeeID"),
            Fk("FK_Orders_Shippers", "dbo", "Orders", "ShipperID", "dbo", "Shippers", "ShipperID"),
            Fk("FK_OrderDetails_Orders", "dbo", "OrderDetails", "OrderID", "dbo", "Orders", "OrderID"),
            Fk("FK_OrderDetails_Products", "dbo", "OrderDetails", "ProductID", "dbo", "Products", "ProductID"),
            Fk("FK_Products_Categories", "dbo", "Products", "CategoryID", "dbo", "Categories", "CategoryID"),
            Fk("FK_Products_Suppliers", "dbo", "Products", "SupplierID", "dbo", "Suppliers", "SupplierID"),
            Fk("FK_Invoices_Orders", "Sales", "Invoices", "OrderID", "dbo", "Orders", "OrderID"),
        ]);
        cache.RebuildFkIndex();

        return cache;
    }

    private static DatabaseObject Table(string schema, string name, params Column[] columns)
        => Obj(schema, name, DbObjectType.Table, columns);

    private static DatabaseObject Proc(string schema, string name, params Parameter[] parameters)
    {
        var obj = Obj(schema, name, DbObjectType.Procedure);
        obj.Parameters.AddRange(parameters);
        return obj;
    }

    private static DatabaseObject Obj(string schema, string name, DbObjectType type, params Column[] columns)
    {
        var obj = new DatabaseObject
        {
            SchemaName = schema,
            ObjectName = name,
            ObjectType = type,
            ColumnsLoaded = true,
            ModifyDate = new DateTime(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc),
        };
        int id = 1;
        foreach (var c in columns)
        {
            c.ColumnId = id++;
            obj.Columns.Add(c);
        }
        return obj;
    }

    private static Column Pk(string name)
        => new() { ColumnName = name, TypeName = "int", IsPrimaryKey = true, IsIdentity = true };

    private static Column Col(string name, string type, int maxLength = 0, int precision = 0, int scale = 0)
        => new() { ColumnName = name, TypeName = type, MaxLength = maxLength, Precision = precision, Scale = scale, IsNullable = true };

    private static Parameter Param(int id, string name, string type)
        => new() { ParameterId = id, ParameterName = name, TypeName = type };

    private static ForeignKey Fk(
        string name,
        string parentSchema, string parentTable, string parentColumn,
        string refSchema, string refTable, string refColumn)
        => new()
        {
            FkName = name,
            ParentSchema = parentSchema,
            ParentTable = parentTable,
            ParentColumns = [parentColumn],
            ReferencedSchema = refSchema,
            ReferencedTable = refTable,
            ReferencedColumns = [refColumn],
        };
}
