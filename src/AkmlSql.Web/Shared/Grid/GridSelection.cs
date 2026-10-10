namespace AkmlSql.Web.Shared.Grid;

/// <summary>A cell in display coordinates: <see cref="Row"/> is the position on screen.</summary>
public readonly record struct GridCell(int Row, int Col);

/// <summary>An inclusive rectangle of cells.</summary>
public readonly record struct GridRect(int Top, int Left, int Bottom, int Right)
{
    public static GridRect Between(GridCell a, GridCell b) =>
        new(Math.Min(a.Row, b.Row), Math.Min(a.Col, b.Col), Math.Max(a.Row, b.Row), Math.Max(a.Col, b.Col));

    public bool Contains(int row, int col) => row >= Top && row <= Bottom && col >= Left && col <= Right;
}

/// <summary>Keyboard moves (FR-010).</summary>
public enum GridMove
{
    Up,
    Down,
    Left,
    Right,
    PageUp,
    PageDown,
    Home,
    End,
    CtrlHome,
    CtrlEnd,
}

/// <summary>
/// Spec 041 (research R4, FR-008/FR-010) — spreadsheet selection over one result set, in DISPLAY
/// coordinates (a row is a position on screen; after a sort a different fetched row sits there).
///
/// <para>The selection is an ordered list of include / exclude rectangles: the last rectangle that
/// covers a cell decides whether it is selected. Select-all is one rectangle, so a 100 000 × 200 result
/// allocates nothing per cell, and Ctrl can remove a cell, a row or a column from any selection
/// (FR-008). A plain click starts a new list; Shift replaces everything since the anchor; Ctrl adds a
/// toggle.</para>
/// </summary>
public sealed class GridSelection
{
    private readonly List<(GridRect Rect, bool Include)> _ops = new();
    // Index of the first operation Shift and drags replace (the one that set the anchor).
    private int _anchorOp;
    private AnchorKind _anchorKind = AnchorKind.Cell;

    private enum AnchorKind { Cell, Row, Column }

    public GridSelection(int rowCount, int columnCount)
    {
        RowCount = Math.Max(0, rowCount);
        ColumnCount = Math.Max(0, columnCount);
    }

    public int RowCount { get; }

    public int ColumnCount { get; }

    /// <summary>Where Shift extends from.</summary>
    public GridCell Anchor { get; private set; }

    /// <summary>The cell keyboard focus is on (the grid's <c>aria-activedescendant</c>).</summary>
    public GridCell Focus { get; private set; }

    /// <summary>True until something is selected (no focus cell either).</summary>
    public bool IsEmpty => _ops.Count == 0;

    /// <summary>How many rectangles the selection is made of (select-all is one).</summary>
    public int OperationCount => _ops.Count;

    /// <summary>True when every cell is selected.</summary>
    public bool IsAll => _ops.Count == 1 && _ops[0].Include && _ops[0].Rect == AllRect;

    /// <summary>Exactly one cell is selected — what a plain click leaves (a second click then edits).</summary>
    public bool IsSingleCell =>
        _ops.Count == 1 && _ops[0].Include
        && _ops[0].Rect.Top == _ops[0].Rect.Bottom && _ops[0].Rect.Left == _ops[0].Rect.Right;

    private bool HasCells => RowCount > 0 && ColumnCount > 0;

    private GridRect AllRect => new(0, 0, RowCount - 1, ColumnCount - 1);

    public bool IsSelected(int row, int col)
    {
        for (int i = _ops.Count - 1; i >= 0; i--)
        {
            if (_ops[i].Rect.Contains(row, col)) return _ops[i].Include;
        }
        return false;
    }

    // ── Pointer ────────────────────────────────────────────────────────────────────────────────

    /// <summary>Click a cell. Shift extends from the anchor; Ctrl toggles the cell.</summary>
    public void Click(int row, int col, bool shift, bool ctrl)
    {
        if (!HasCells) return;
        var cell = Clamp(row, col);
        if (shift && !IsEmpty)
        {
            Extend(cell);
            return;
        }
        if (ctrl && !IsEmpty)
        {
            AddToggle(new GridRect(cell.Row, cell.Col, cell.Row, cell.Col), IsSelected(cell.Row, cell.Col));
            SetAnchor(cell, AnchorKind.Cell);
            return;
        }
        _ops.Clear();
        _ops.Add((new GridRect(cell.Row, cell.Col, cell.Row, cell.Col), true));
        _anchorOp = 0;
        SetAnchor(cell, AnchorKind.Cell);
    }

    /// <summary>Click a row number: the whole row.</summary>
    public void ClickRowHeader(int row, bool shift, bool ctrl)
    {
        if (!HasCells) return;
        row = Math.Clamp(row, 0, RowCount - 1);
        var rect = new GridRect(row, 0, row, ColumnCount - 1);
        if (shift && !IsEmpty)
        {
            ReplaceFromAnchor(new GridRect(Math.Min(Anchor.Row, row), 0, Math.Max(Anchor.Row, row), ColumnCount - 1));
            Focus = new GridCell(row, Focus.Col);
            return;
        }
        if (ctrl && !IsEmpty)
        {
            AddToggle(rect, RowFullySelected(row));
        }
        else
        {
            _ops.Clear();
            _ops.Add((rect, true));
            _anchorOp = 0;
        }
        SetAnchor(new GridCell(row, IsEmpty ? 0 : Math.Clamp(Focus.Col, 0, ColumnCount - 1)), AnchorKind.Row);
    }

    /// <summary>Click a column header: the whole column.</summary>
    public void ClickColumnHeader(int col, bool shift, bool ctrl)
    {
        if (!HasCells) return;
        col = Math.Clamp(col, 0, ColumnCount - 1);
        var rect = new GridRect(0, col, RowCount - 1, col);
        if (shift && !IsEmpty)
        {
            ReplaceFromAnchor(new GridRect(0, Math.Min(Anchor.Col, col), RowCount - 1, Math.Max(Anchor.Col, col)));
            Focus = new GridCell(Focus.Row, col);
            return;
        }
        if (ctrl && !IsEmpty)
        {
            AddToggle(rect, ColumnFullySelected(col));
        }
        else
        {
            _ops.Clear();
            _ops.Add((rect, true));
            _anchorOp = 0;
        }
        SetAnchor(new GridCell(IsEmpty ? 0 : Math.Clamp(Focus.Row, 0, RowCount - 1), col), AnchorKind.Column);
    }

    /// <summary>The corner, or Ctrl+A.</summary>
    public void SelectAll()
    {
        if (!HasCells) return;
        _ops.Clear();
        _ops.Add((AllRect, true));
        _anchorOp = 0;
        if (Focus.Row >= RowCount || Focus.Col >= ColumnCount) Focus = new GridCell(0, 0);
        Anchor = new GridCell(0, 0);
        _anchorKind = AnchorKind.Cell;
    }

    /// <summary>A pointer drag reached <paramref name="row"/>, <paramref name="col"/>: select the
    /// rectangle from the anchor.</summary>
    public void DragTo(int row, int col)
    {
        if (!HasCells || IsEmpty) return;
        Extend(Clamp(row, col));
    }

    public void Clear()
    {
        _ops.Clear();
        _anchorOp = 0;
        Anchor = Focus = default;
        _anchorKind = AnchorKind.Cell;
    }

    // ── Keyboard ───────────────────────────────────────────────────────────────────────────────

    /// <summary>Move the focus; with <paramref name="extend"/> (Shift) extend from the anchor.</summary>
    public void Move(GridMove move, bool extend, int pageRows)
    {
        if (!HasCells) return;
        if (IsEmpty)
        {
            Click(0, 0, false, false);
            return;
        }
        int r = Focus.Row, c = Focus.Col;
        int page = Math.Max(1, pageRows);
        switch (move)
        {
            case GridMove.Up: r--; break;
            case GridMove.Down: r++; break;
            case GridMove.Left: c--; break;
            case GridMove.Right: c++; break;
            case GridMove.PageUp: r -= page; break;
            case GridMove.PageDown: r += page; break;
            case GridMove.Home: c = 0; break;
            case GridMove.End: c = ColumnCount - 1; break;
            case GridMove.CtrlHome: r = 0; c = 0; break;
            case GridMove.CtrlEnd: r = RowCount - 1; c = ColumnCount - 1; break;
        }
        var target = Clamp(r, c);
        if (extend)
        {
            if (_anchorKind != AnchorKind.Cell) { _anchorKind = AnchorKind.Cell; }
            Extend(target);
        }
        else
        {
            Click(target.Row, target.Col, false, false);
        }
    }

    // ── Reading ────────────────────────────────────────────────────────────────────────────────

    /// <summary>Every selected cell, row by row in display order, columns ascending.</summary>
    public IEnumerable<GridCell> EnumerateSelected()
    {
        if (!HasCells || IsEmpty) yield break;
        var bounds = IncludedBounds();
        for (int r = bounds.Top; r <= bounds.Bottom; r++)
            for (int c = bounds.Left; c <= bounds.Right; c++)
                if (IsSelected(r, c)) yield return new GridCell(r, c);
    }

    /// <summary>Display rows that hold at least one selected cell, ascending.</summary>
    public IReadOnlyList<int> SelectedRows()
    {
        var rows = new List<int>();
        if (!HasCells || IsEmpty) return rows;
        var b = IncludedBounds();
        for (int r = b.Top; r <= b.Bottom; r++)
            for (int c = b.Left; c <= b.Right; c++)
                if (IsSelected(r, c)) { rows.Add(r); break; }
        return rows;
    }

    /// <summary>Columns that hold at least one selected cell, ascending.</summary>
    public IReadOnlyList<int> SelectedColumns()
    {
        var cols = new List<int>();
        if (!HasCells || IsEmpty) return cols;
        var b = IncludedBounds();
        for (int c = b.Left; c <= b.Right; c++)
            for (int r = b.Top; r <= b.Bottom; r++)
                if (IsSelected(r, c)) { cols.Add(c); break; }
        return cols;
    }

    // ── Sorting ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A sort changed which fetched row each display row shows. A selection of whole columns (or
    /// everything) covers every row and stays as it is; anything else collapses to the focused cell,
    /// which follows its fetched row to its new display position.
    /// </summary>
    public void RemapThroughPermutation(IReadOnlyList<int> oldDisplayToFetched, IReadOnlyList<int> newDisplayToFetched)
    {
        if (!HasCells || IsEmpty) return;
        bool rowInvariant = _ops.All(o => o.Rect.Top == 0 && o.Rect.Bottom == RowCount - 1);
        int fetched = Focus.Row >= 0 && Focus.Row < oldDisplayToFetched.Count ? oldDisplayToFetched[Focus.Row] : -1;
        int newRow = Focus.Row;
        for (int i = 0; i < newDisplayToFetched.Count; i++)
        {
            if (newDisplayToFetched[i] == fetched) { newRow = i; break; }
        }
        if (rowInvariant)
        {
            Focus = new GridCell(newRow, Focus.Col);
            Anchor = new GridCell(newRow, Anchor.Col);
            return;
        }
        Click(newRow, Focus.Col, false, false);
    }

    // ── Internals ──────────────────────────────────────────────────────────────────────────────

    private GridCell Clamp(int row, int col) =>
        new(Math.Clamp(row, 0, RowCount - 1), Math.Clamp(col, 0, ColumnCount - 1));

    private void SetAnchor(GridCell cell, AnchorKind kind)
    {
        Anchor = cell;
        Focus = cell;
        _anchorKind = kind;
    }

    private void Extend(GridCell target)
    {
        GridRect rect = _anchorKind switch
        {
            AnchorKind.Row => new GridRect(Math.Min(Anchor.Row, target.Row), 0, Math.Max(Anchor.Row, target.Row), ColumnCount - 1),
            AnchorKind.Column => new GridRect(0, Math.Min(Anchor.Col, target.Col), RowCount - 1, Math.Max(Anchor.Col, target.Col)),
            _ => GridRect.Between(Anchor, target),
        };
        ReplaceFromAnchor(rect);
        Focus = target;
    }

    private void ReplaceFromAnchor(GridRect rect)
    {
        if (_anchorOp < _ops.Count) _ops.RemoveRange(_anchorOp, _ops.Count - _anchorOp);
        _ops.Add((rect, true));
    }

    private void AddToggle(GridRect rect, bool currentlySelected)
    {
        _ops.Add((rect, !currentlySelected));
        _anchorOp = _ops.Count - 1;
    }

    private bool RowFullySelected(int row)
    {
        for (int c = 0; c < ColumnCount; c++) if (!IsSelected(row, c)) return false;
        return true;
    }

    private bool ColumnFullySelected(int col)
    {
        for (int r = 0; r < RowCount; r++) if (!IsSelected(r, col)) return false;
        return true;
    }

    private GridRect IncludedBounds()
    {
        int top = int.MaxValue, left = int.MaxValue, bottom = -1, right = -1;
        foreach (var (rect, include) in _ops)
        {
            if (!include) continue;
            top = Math.Min(top, rect.Top);
            left = Math.Min(left, rect.Left);
            bottom = Math.Max(bottom, rect.Bottom);
            right = Math.Max(right, rect.Right);
        }
        if (bottom < 0) return new GridRect(0, 0, -1, -1);
        return new GridRect(top, left, Math.Min(bottom, RowCount - 1), Math.Min(right, ColumnCount - 1));
    }
}
