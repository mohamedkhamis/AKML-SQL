using AkmlSql.Core;

namespace AkmlSql.Web.Services;

/// <summary>
/// Review finding WEB-10: a Format reply may only replace the document it was asked for. A PostgreSQL format crosses the
/// engine bridge, so its reply can arrive after the user started another Format, opened or created another document, or
/// switched to a connection of the other kind. Each Format takes a ticket; a reply whose ticket is no longer the latest,
/// or whose document or dialect has changed, is dropped. (Typing in the same document is caught at the moment of
/// replacement: the editor replaces the text only if it still reads <see cref="Ticket.Text"/>.)
/// </summary>
public sealed class FormatRequestGate
{
    private int _latest;

    /// <summary>What one Format was asked for: its number, the text sent, the document and the dialect.</summary>
    public readonly record struct Ticket(int Number, string Text, string? Document, DatabaseKind Kind);

    public Ticket Begin(string text, string? document, DatabaseKind kind) => new(++_latest, text ?? string.Empty, document, kind);

    /// <summary>No later Format has started.</summary>
    public bool IsLatest(Ticket ticket) => ticket.Number == _latest;

    /// <summary>The reply may still replace the document: it is the latest Format, for the same document and dialect.</summary>
    public bool Applies(Ticket ticket, string? document, DatabaseKind kind) =>
        IsLatest(ticket) && string.Equals(ticket.Document, document, System.StringComparison.Ordinal) && ticket.Kind == kind;
}
