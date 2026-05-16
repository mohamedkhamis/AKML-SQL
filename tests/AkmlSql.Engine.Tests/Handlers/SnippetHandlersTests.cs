using System;
using System.IO;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine;
using AkmlSql.Engine.Handlers.Snippets;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Server;
using AkmlSql.Engine.Snippets;
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
}
