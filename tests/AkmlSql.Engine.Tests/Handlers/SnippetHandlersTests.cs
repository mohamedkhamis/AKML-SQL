using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine;
using AkmlSql.Engine.Handlers.Snippets;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Server;
using AkmlSql.Engine.Snippets;
using AkmlSql.Engine.Snippets.Models;
using AkmlSql.Engine.Transports;
using Xunit;

namespace AkmlSql.Engine.Tests.Handlers;

/// <summary>
/// Spec 021 (web edition) -- M0.3 task T015. Smoke tests for the five typed snippet handlers
/// to verify the wiring (RequestMessageType / ResponseMessageType pairing). Functional
/// behaviour of <see cref="SnippetRequestHandler"/> stays covered by the existing
/// SnippetIndexTests / SnippetModelsTests.
/// </summary>
public sealed class SnippetHandlersTests : IDisposable
{
    private readonly string _personalDir;
    private readonly string _builtInDir;
    private readonly SnippetRequestHandler _inner;

    public SnippetHandlersTests()
    {
        _personalDir = Path.Combine(Path.GetTempPath(), $"akml_sn_personal_{Guid.NewGuid():N}");
        _builtInDir = Path.Combine(Path.GetTempPath(), $"akml_sn_builtin_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_personalDir);
        Directory.CreateDirectory(_builtInDir);
        _inner = new SnippetRequestHandler(_personalDir, _builtInDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_personalDir, recursive: true); } catch { }
        try { Directory.Delete(_builtInDir, recursive: true); } catch { }
    }

    [Fact]
    public void Snippet_handlers_advertise_correct_message_type_pairs()
    {
        Assert.Equal((MessageTypes.SnippetExpand, MessageTypes.SnippetExpandResult),
            (new SnippetExpandHandler(_inner).RequestMessageType, new SnippetExpandHandler(_inner).ResponseMessageType));
        Assert.Equal((MessageTypes.SnippetList, MessageTypes.SnippetListResult),
            (new SnippetListHandler(_inner).RequestMessageType, new SnippetListHandler(_inner).ResponseMessageType));
        Assert.Equal((MessageTypes.SnippetSave, MessageTypes.SnippetSaveResult),
            (new SnippetSaveHandler(_inner).RequestMessageType, new SnippetSaveHandler(_inner).ResponseMessageType));
        Assert.Equal((MessageTypes.SnippetDelete, MessageTypes.SnippetDeleteResult),
            (new SnippetDeleteHandler(_inner).RequestMessageType, new SnippetDeleteHandler(_inner).ResponseMessageType));
        Assert.Equal((MessageTypes.SnippetImport, MessageTypes.SnippetImportResult),
            (new SnippetImportHandler(_inner).RequestMessageType, new SnippetImportHandler(_inner).ResponseMessageType));
    }

    /// <summary>
    /// Spec 030 T046 / FR-036 — the regression this fix targets: a snippet's custom Variables must
    /// survive Save → reload → List so the Snippet Manager can edit/re-save them without wiping. This
    /// proves the engine transport end-to-end (HandleSave persists Variables; HandleList re-emits them
    /// on SnippetInfo.Variables with the name/default/tooltip/schemaAware JSON contract intact).
    /// </summary>
    [Fact]
    public void HandleSave_then_HandleList_round_trips_custom_variables()
    {
        var snippet = new Snippet
        {
            Metadata = new SnippetMetadata { Shortcode = "rtvar", Name = "RoundTrip Var", Category = "Custom" },
            Body = new[] { "SELECT * FROM $tbl$ WHERE id = $id$;" },
            Variables = new[]
            {
                new SnippetVariable { Name = "tbl", Default = "dbo.Customers", Tooltip = "target table", SchemaAware = "table" },
                new SnippetVariable { Name = "id",  Default = "1",             Tooltip = "row id" },
            },
        };

        var save = _inner.HandleSave(new SnippetSaveRequest { SnippetJson = JsonSerializer.Serialize(snippet), IsNew = true });
        Assert.True(save.Success, save.ErrorMessage);

        var list = _inner.HandleList(new SnippetListRequest());
        var info = Assert.Single(list.Snippets, s => s.Shortcode == "rtvar");

        Assert.Equal(2, info.Variables.Length);
        var tbl = Assert.Single(info.Variables, v => v.Name == "tbl");
        Assert.Equal("dbo.Customers", tbl.Default);
        Assert.Equal("target table", tbl.Tooltip);
        Assert.Equal("table", tbl.SchemaAware);
        var id = Assert.Single(info.Variables, v => v.Name == "id");
        Assert.Equal("1", id.Default);
        Assert.Null(id.SchemaAware);
    }
}
