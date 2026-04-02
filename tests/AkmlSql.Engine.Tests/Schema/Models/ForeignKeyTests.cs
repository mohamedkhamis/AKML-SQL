using Xunit;
using AkmlSql.Engine.Schema.Models;

namespace AkmlSql.Engine.Tests.Schema.Models;

public class ForeignKeyTests
{
    [Fact]
    public void ForeignKey_Defaults()
    {
        var fk = new ForeignKey();
        Assert.Equal(string.Empty, fk.FkName);
        Assert.Equal(string.Empty, fk.ParentSchema);
        Assert.Equal(string.Empty, fk.ParentTable);
        Assert.Empty(fk.ParentColumns);
        Assert.Equal(string.Empty, fk.ReferencedSchema);
        Assert.Equal(string.Empty, fk.ReferencedTable);
        Assert.Empty(fk.ReferencedColumns);
        Assert.False(fk.IsDisabled);
        Assert.Equal(FkAction.NoAction, fk.DeleteAction);
        Assert.Equal(FkAction.NoAction, fk.UpdateAction);
    }

    [Fact]
    public void ForeignKey_ParentFullName()
    {
        var fk = new ForeignKey { ParentSchema = "dbo", ParentTable = "Orders" };
        Assert.Equal("dbo.Orders", fk.ParentFullName);
    }

    [Fact]
    public void ForeignKey_ReferencedFullName()
    {
        var fk = new ForeignKey { ReferencedSchema = "dbo", ReferencedTable = "Customers" };
        Assert.Equal("dbo.Customers", fk.ReferencedFullName);
    }

    [Fact]
    public void ForeignKey_Mutations()
    {
        var fk = new ForeignKey
        {
            FkName = "FK_Orders_Customers",
            ParentSchema = "sales",
            ParentTable = "Orders",
            ReferencedSchema = "dbo",
            ReferencedTable = "Customers",
            IsDisabled = true,
            DeleteAction = FkAction.Cascade,
            UpdateAction = FkAction.SetNull
        };
        fk.ParentColumns.Add("CustomerId");
        fk.ReferencedColumns.Add("Id");

        Assert.Equal("FK_Orders_Customers", fk.FkName);
        Assert.Equal("sales.Orders", fk.ParentFullName);
        Assert.Equal("dbo.Customers", fk.ReferencedFullName);
        Assert.True(fk.IsDisabled);
        Assert.Equal(FkAction.Cascade, fk.DeleteAction);
        Assert.Equal(FkAction.SetNull, fk.UpdateAction);
        Assert.Single(fk.ParentColumns);
        Assert.Single(fk.ReferencedColumns);
    }

    [Fact]
    public void FkAction_Values()
    {
        Assert.Equal(0, (int)FkAction.NoAction);
        Assert.Equal(1, (int)FkAction.Cascade);
        Assert.Equal(2, (int)FkAction.SetNull);
        Assert.Equal(3, (int)FkAction.SetDefault);
    }
}
