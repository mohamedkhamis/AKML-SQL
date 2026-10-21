namespace AkmlSql.Web.Shared;

/// <summary>Spec 041 (R24) — what <c>akml-editor.js</c> <c>getSelectionInfo</c> reports: the selected text and
/// the 1-based document line it starts on. Spec 042: and the 1-based column it starts at on that line.</summary>
public sealed record EditorSelectionInfo(string Text, int StartLine, int StartColumn = 1);
