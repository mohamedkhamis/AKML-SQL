namespace AkmlSql.Web.Shared.Grid;

/// <summary>
/// Spec 041 (research R1/R2, FR-001–FR-007) — the column widths of ONE result set. Every row of the
/// grid (header, virtualised body, new rows) shares the single track list <see cref="ToCssTracks"/>
/// emits into the grid root's <c>--akml-grid-cols</c>, so widths never reflow while the user scrolls,
/// resizes the window or toggles panels. A new result gets a new layout: widths belong to the result
/// they were set on.
///
/// <para>Fitting: <see cref="SelectCandidates"/> picks the header and the three longest display texts of
/// a column (scanning every row, so a long value far down is never cut); the browser measures those few
/// strings with a canvas; <see cref="Fit"/> turns the measurements into a width between the header floor
/// and <see cref="MaxColumnPx"/>. Without the browser (tests, a disposal race) <see cref="EstimatePx"/>
/// stands in, so the grid never renders unsized.</para>
/// </summary>
public sealed class GridColumnLayout
{
    /// <summary>Narrowest a column gets; keeps a short header readable.</summary>
    public const int MinColumnPx = 48;

    /// <summary>Widest a fitted column gets, so one long text column cannot push the others off screen.
    /// The user can drag past it.</summary>
    public const int MaxColumnPx = 480;

    /// <summary>Width before the first fit.</summary>
    public const int DefaultColumnPx = 120;

    /// <summary>A cell's left + right padding (results-grid.css).</summary>
    public const int CellPaddingPx = 16;

    /// <summary>The right border also takes space inside a cell's CSS grid track.</summary>
    public const int CellBorderPx = 1;

    /// <summary>Room a header needs beyond its text: the sort control and the resize handle.</summary>
    public const int HeaderAllowancePx = 28;

    /// <summary>Most widest-value candidates measured per column.</summary>
    public const int MaxCandidates = 3;

    /// <summary>Character width used when the browser cannot measure (the grid font is 12 px).</summary>
    public const double FallbackCharPx = 7.0;

    /// <summary>Widest a user can drag a column.</summary>
    public const int MaxUserColumnPx = 4_000;

    private readonly int[] _fitted;
    private readonly int?[] _user;

    public GridColumnLayout(int columnCount, int rowCount)
    {
        if (columnCount < 0) columnCount = 0;
        _fitted = Enumerable.Repeat(DefaultColumnPx, columnCount).ToArray();
        _user = new int?[columnCount];
        RowNumberWidthPx = RowNumberWidth(rowCount);
    }

    public int ColumnCount => _fitted.Length;

    /// <summary>Width of the row-number column.</summary>
    public int RowNumberWidthPx { get; private set; }

    /// <summary>The result's row count changed in place (an Apply deleted or added rows): resize the
    /// row-number track and keep every column width.</summary>
    public void SetRowCount(int rowCount) => RowNumberWidthPx = RowNumberWidth(rowCount);

    /// <summary>The effective width of each data column: the user's, else the fitted one.</summary>
    public IReadOnlyList<int> WidthsPx => _fitted.Select((w, i) => _user[i] ?? w).ToArray();

    /// <summary>The effective width of one column (0 when out of range).</summary>
    public int WidthPx(int col) => col >= 0 && col < _fitted.Length ? _user[col] ?? _fitted[col] : 0;

    public bool IsUserSet(int col) => col >= 0 && col < _user.Length && _user[col].HasValue;

    /// <summary>Record a fitted width. A width the user set stays in force until <see cref="ResetWidths"/>.</summary>
    public void ApplyFit(int col, int px)
    {
        if (col < 0 || col >= _fitted.Length) return;
        _fitted[col] = Math.Clamp(px, MinColumnPx, MaxColumnPx);
    }

    /// <summary>"Auto-fit this column" / an edge double-click: the fitted width replaces the user's.</summary>
    public void AutoFit(int col, int px)
    {
        if (col < 0 || col >= _fitted.Length) return;
        _user[col] = null;
        ApplyFit(col, px);
    }

    /// <summary>A drag or a keyboard resize. May exceed <see cref="MaxColumnPx"/>; never below the minimum.</summary>
    public void SetUserWidth(int col, int px)
    {
        if (col < 0 || col >= _user.Length) return;
        _user[col] = Math.Clamp(px, MinColumnPx, MaxUserColumnPx);
    }

    /// <summary>"Reset column widths": every column back to its fitted width.</summary>
    public void ResetWidths()
    {
        for (int i = 0; i < _user.Length; i++) _user[i] = null;
    }

    /// <summary>The <c>--akml-grid-cols</c> value: the row-number track, then one track per column.</summary>
    public string ToCssTracks()
    {
        var parts = new string[_fitted.Length + 1];
        parts[0] = RowNumberWidthPx + "px";
        for (int i = 0; i < _fitted.Length; i++) parts[i + 1] = (_user[i] ?? _fitted[i]) + "px";
        return string.Join(" ", parts);
    }

    /// <summary>Row-number column width for a result of <paramref name="rowCount"/> rows: wide enough for
    /// the largest number (8 px a digit) plus room for the edited/new/deleted glyph.</summary>
    public static int RowNumberWidth(int rowCount)
    {
        int digits = Math.Max(1, (int)Math.Floor(Math.Log10(Math.Max(1, rowCount))) + 1);
        return Math.Max(36, 20 + digits * 8);
    }

    /// <summary>The strings worth measuring for one column: its header and its three longest values.</summary>
    public static FitCandidates SelectCandidates(string header, IEnumerable<string?> displayTexts)
    {
        // A running top-3 by length: one pass, no sort of the whole column.
        var top = new List<string>(MaxCandidates + 1);
        foreach (var text in displayTexts)
        {
            if (string.IsNullOrEmpty(text)) continue;
            if (top.Count == MaxCandidates && text.Length <= top[^1].Length) continue;
            int at = top.FindIndex(t => text.Length > t.Length);
            if (at < 0) top.Add(text); else top.Insert(at, text);
            if (top.Count > MaxCandidates) top.RemoveAt(top.Count - 1);
        }
        return new FitCandidates(header ?? string.Empty, top);
    }

    /// <summary>The fitted width for measured header and widest-value widths (text only, in px).</summary>
    public static int Fit(double headerPx, double valuePx)
    {
        double headerNeed = headerPx + CellPaddingPx + CellBorderPx + HeaderAllowancePx;
        double valueNeed = valuePx + CellPaddingPx + CellBorderPx;
        double floor = Math.Max(MinColumnPx, Math.Min(headerNeed, MaxColumnPx));
        double width = Math.Max(headerNeed, valueNeed);
        return (int)Math.Ceiling(Math.Clamp(width, floor, MaxColumnPx));
    }

    /// <summary>Text width when the browser cannot measure.</summary>
    public static double EstimatePx(string? text) => (text?.Length ?? 0) * FallbackCharPx;
}

/// <summary>What <see cref="GridColumnLayout.SelectCandidates"/> chose to measure for one column.</summary>
public sealed record FitCandidates(string Header, IReadOnlyList<string> Values);
