namespace AkmlSql.Web.Shared.Grid;

/// <summary>How the user left the inline cell editor (spec 041 FR-013).</summary>
public enum GridEditorExit
{
    /// <summary>The value changed, or the input lost focus: keep the value, stay where you are.</summary>
    Commit,

    /// <summary>Enter: keep the value and give the grid its focus back.</summary>
    Enter,

    /// <summary>Escape: drop the value; the cell shows what it held before.</summary>
    Cancel,

    /// <summary>Tab: keep the value and edit the next editable cell.</summary>
    Tab,

    /// <summary>Shift+Tab: keep the value and edit the previous editable cell.</summary>
    TabBack,
}

/// <summary>What <see cref="GridCellEditor"/> reports when the user is done.</summary>
public readonly record struct GridEditorResult(GridEditorExit Exit, string Value);
