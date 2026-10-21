using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;
using AkmlSql.Web.Shared;
using AkmlSql.Web.Shared.Grid;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Web.Tests.Grid;

/// <summary>Records Apply requests; answers with <see cref="ApplyReply"/> (success by default).</summary>
internal sealed class RecordingQueryExecution : IQueryExecutionService
{
    public List<ApplyChangesRequest> Applied { get; } = new();

    public ApplyChangesResult ApplyReply { get; set; } = new() { Status = ExecuteStatus.Ok };

    public Task<ExecuteQueryResult> ExecuteAsync(string sql, int maxRows, int timeoutSeconds, string queryId, CancellationToken ct)
        => Task.FromResult(new ExecuteQueryResult { QueryId = queryId, Status = ExecuteStatus.Ok });

    public Task CancelAsync(string queryId, CancellationToken ct) => Task.CompletedTask;

    /// <summary>When set, Apply waits for it before answering (a slow engine).</summary>
    public TaskCompletionSource? ApplyGate { get; set; }

    public async Task<ApplyChangesResult> ApplyAsync(ApplyChangesRequest request, CancellationToken ct)
    {
        Applied.Add(request);
        if (ApplyGate != null) await ApplyGate.Task;
        return ApplyReply;
    }
}

/// <summary>
/// Spec 041 (T046–T052) — renders <see cref="ResultSetGrid"/> the way the grid tests need it: Loose JS
/// interop (research R68), recording fakes for notifications, dialogs and Apply, and helpers that drive the
/// grid the way the browser does. Cell presses and clicks come from akml-results-grid.js in the browser, so
/// the helpers call the same [JSInvokable] entry points the module calls.
/// </summary>
internal sealed class GridTestHost : IDisposable
{
    public GridTestHost()
    {
        Context.JSInterop.Mode = JSRuntimeMode.Loose;
        Context.Services.AddSingleton<IQueryExecutionService>(Exec);
        Context.Services.AddSingleton<INotificationService>(Notes);
        Context.Services.AddSingleton<IDialogService>(Dialogs);
    }

    public BunitContext Context { get; } = new();
    public RecordingQueryExecution Exec { get; } = new();
    public FakeNotificationService Notes { get; } = new();
    public FakeDialogService Dialogs { get; } = new();

    public IRenderedComponent<ResultSetGrid> Render(ExecuteResultSet set, bool showColumnTypes = false, bool retainLineBreaks = false, string? documentName = null) =>
        Context.Render<ResultSetGrid>(p => p
            .Add(x => x.Set, set)
            .Add(x => x.SetIndex, 0)
            .Add(x => x.ShowColumnTypes, showColumnTypes)
            .Add(x => x.RetainLineBreaksOnCopy, retainLineBreaks)
            .Add(x => x.DocumentName, documentName));

    public void Dispose() => Context.Dispose();

    // ── Data ───────────────────────────────────────────────────────────────────────────────────────

    /// <summary>id int, name nvarchar(50), created datetime — not editable.</summary>
    public static ExecuteResultSet ReadOnlySet(params string?[][] rows) => new()
    {
        ColumnNames = new[] { "id", "name", "created" },
        ColumnSqlTypes = new[] { "int", "nvarchar(50)", "datetime" },
        ClrTypeHints = new[] { ClrTypeHint.Int64, ClrTypeHint.String, ClrTypeHint.DateTime },
        Rows = rows.Length > 0 ? rows : new[]
        {
            new string?[] { "1", "alpha", "2025-10-01T08:00:00.0000000" },
            new string?[] { "2", "bravo", "2025-10-02T08:00:00.0000000" },
            new string?[] { "3", "charlie", "2025-10-03T08:00:00.0000000" },
            new string?[] { "4", "delta", "2025-10-04T08:00:00.0000000" },
            new string?[] { "5", "echo", "2025-10-05T08:00:00.0000000" },
        },
    };

    /// <summary>
    /// dbo.demo: id int PK identity, name nvarchar(50) NULL, qty int NOT NULL, flag bit NULL,
    /// blob varbinary(16) NULL.
    /// </summary>
    public static ExecuteResultSet EditableSet(params string?[][] rows) => new()
    {
        ColumnNames = new[] { "id", "name", "qty", "flag", "blob" },
        ColumnSqlTypes = new[] { "int", "nvarchar(50)", "int", "bit", "varbinary(16)" },
        ClrTypeHints = new[] { ClrTypeHint.Int64, ClrTypeHint.String, ClrTypeHint.Int64, ClrTypeHint.Bool, ClrTypeHint.Binary },
        Rows = rows.Length > 0 ? rows : new[]
        {
            new string?[] { "1", "alpha", "10", "1", "AQI=" },
            new string?[] { "2", "bravo", "2", "0", null },
            new string?[] { "3", null, "33", null, null },
        },
        IsEditable = true,
        DefaultsKnown = true,   // the engine read dbo.demo's columns (a warm schema cache)
        BaseSchema = "dbo",
        BaseTable = "demo",
        Provenance = new[]
        {
            new ColumnProvenanceDto { Ordinal = 0, BaseColumnName = "id", IsKey = true, IsTruePrimaryKey = true, IsAutoIncrement = true },
            new ColumnProvenanceDto { Ordinal = 1, BaseColumnName = "name", AllowDBNull = true },
            new ColumnProvenanceDto { Ordinal = 2, BaseColumnName = "qty", AllowDBNull = false },
            new ColumnProvenanceDto { Ordinal = 3, BaseColumnName = "flag", AllowDBNull = true },
            new ColumnProvenanceDto { Ordinal = 4, BaseColumnName = "blob", AllowDBNull = true },
        },
    };

    // ── Driving the grid ───────────────────────────────────────────────────────────────────────────

    public static AngleSharp.Dom.IElement Root(IRenderedComponent<ResultSetGrid> cut) => cut.Find("[data-testid=results-grid]");

    public static AngleSharp.Dom.IElement Cell(IRenderedComponent<ResultSetGrid> cut, int r, int c) => cut.Find($"[data-testid=results-cell-{r}-{c}]");

    /// <summary>A plain click on a data cell, as the browser reports it: press, then click.</summary>
    public static void ClickCell(IRenderedComponent<ResultSetGrid> cut, int displayRow, int col, bool shift = false, bool ctrl = false)
    {
        cut.InvokeAsync(() => cut.Instance.OnCellPointerDownFromJs(displayRow, col, shift, ctrl)).GetAwaiter().GetResult();
        if (!shift && !ctrl) cut.InvokeAsync(() => cut.Instance.OnCellClickFromJs(displayRow, col)).GetAwaiter().GetResult();
    }

    public static void Press(IRenderedComponent<ResultSetGrid> cut, string key, bool ctrl = false, bool shift = false, bool alt = false) =>
        Root(cut).KeyDown(new KeyboardEventArgs { Key = key, CtrlKey = ctrl, ShiftKey = shift, AltKey = alt });

    public static void MouseDown(AngleSharp.Dom.IElement element, bool shift = false, bool ctrl = false) =>
        element.MouseDown(new MouseEventArgs { Button = 0, ShiftKey = shift, CtrlKey = ctrl });

    public static void OpenCellMenu(IRenderedComponent<ResultSetGrid> cut, int displayRow, int col) =>
        cut.InvokeAsync(() => cut.Instance.OnContextMenuFromJs(40, 60, displayRow, col, false)).GetAwaiter().GetResult();

    public static void OpenHeaderMenu(IRenderedComponent<ResultSetGrid> cut, int col) =>
        cut.InvokeAsync(() => cut.Instance.OnContextMenuFromJs(40, 20, -1, col, true)).GetAwaiter().GetResult();

    /// <summary>The --akml-grid-cols tracks on the grid root, in pixels (row-number track first).</summary>
    public static int[] Tracks(IRenderedComponent<ResultSetGrid> cut)
    {
        var style = Root(cut).GetAttribute("style") ?? string.Empty;
        const string prefix = "--akml-grid-cols:";
        int at = style.IndexOf(prefix, StringComparison.Ordinal);
        Assert.True(at >= 0, $"no {prefix} in '{style}'");
        var value = style.Substring(at + prefix.Length).Split(';')[0].Trim();
        return value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(t => int.Parse(t.Replace("px", ""))).ToArray();
    }

    /// <summary>The fetched row index of each body row, top to bottom.</summary>
    public static int[] DisplayOrder(IRenderedComponent<ResultSetGrid> cut) =>
        cut.FindAll("[data-testid^=results-row-]")
            .Select(e => int.Parse(e.GetAttribute("data-testid")!.Substring("results-row-".Length)))
            .ToArray();

    /// <summary>The labels of the open menu's items, separators as "—".</summary>
    public static string[] MenuLabels(IRenderedComponent<ResultSetGrid> cut, string testId) =>
        cut.Find($"[data-testid={testId}]").Children
            .Select(li => li.GetAttribute("role") == "separator" ? "—" : li.QuerySelector(".akml-menu-label")?.TextContent.Trim() ?? "")
            .ToArray();

    /// <summary>The widths fitted from the 7 px-per-character estimate (interop unavailable).</summary>
    public static int EstimatedFit(string header, params string[] values) =>
        GridColumnLayout.Fit(GridColumnLayout.EstimatePx(header), values.Length == 0 ? 0 : values.Max(GridColumnLayout.EstimatePx));
}
