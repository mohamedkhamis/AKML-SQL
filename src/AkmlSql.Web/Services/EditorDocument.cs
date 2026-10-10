using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AkmlSql.Web.Services;

/// <summary>A file read for the editor: its text, or why it was refused.</summary>
public readonly record struct DecodedFile(string? Text, string? Error);

/// <summary>
/// Spec 041 (research R64, FR-072, FR-089) — the editor's document, pure: SSMS's default names
/// (<c>SQLQuery{n}</c>), a file name made safe to save under, the browser title SSMS's window title reads like, and
/// a file decoded as SSMS decodes one.
/// </summary>
public static class EditorDocument
{
    public const string DefaultPrefix = "SQLQuery";

    private static readonly Regex DefaultNamePattern = new("^SQLQuery[0-9]+$", RegexOptions.CultureInvariant);

    // Characters no file system accepts in a name (Windows' set, which covers the others).
    private static readonly char[] InvalidFileNameChars =
        Path.GetInvalidFileNameChars().Concat(new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' }).Distinct().ToArray();

    /// <summary><c>SQLQuery{n}</c>; n below 1 reads as 1.</summary>
    public static string DefaultName(int n) => DefaultPrefix + Math.Max(1, n);

    /// <summary>True for a name the editor gave (<c>SQLQuery{n}</c>), not one the user chose.</summary>
    public static bool IsDefaultName(string? name) => name != null && DefaultNamePattern.IsMatch(name);

    /// <summary>
    /// A name to save under: the characters a file name cannot hold removed, the extension dropped, spaces and dots
    /// trimmed; nothing left gives the next default name.
    /// </summary>
    public static string SanitizeFileName(string? name, int nextDefaultNumber)
    {
        var raw = (name ?? string.Empty).Trim();
        int dot = raw.LastIndexOf('.');
        if (dot > 0) raw = raw.Substring(0, dot);   // "monthly.close.sql" keeps "monthly.close"
        var cleaned = new string(raw.Where(c => !InvalidFileNameChars.Contains(c) && !char.IsControl(c)).ToArray())
            .Trim().Trim('.').Trim();
        return cleaned.Length == 0 ? DefaultName(nextDefaultNumber) : cleaned;
    }

    /// <summary>The browser title: <c>orders-audit* - SERVER.db - AKML SQL</c>, or <c>SQLQuery1 - AKML SQL</c>
    /// with no connection.</summary>
    public static string TitleFor(string name, bool modified, string? server, string? database)
    {
        var title = name + (modified ? "*" : string.Empty);
        if (!string.IsNullOrEmpty(server))
            title += string.IsNullOrEmpty(database) ? $" - {server}" : $" - {server}.{database}";
        return title + " - AKML SQL";
    }

    /// <summary>
    /// Read a file: UTF-8 with or without a BOM, UTF-16 LE or BE with a BOM. Refused above the 10 MB limit — counted
    /// in characters and in UTF-8 bytes, since the document travels to the engine as UTF-8.
    /// </summary>
    public static DecodedFile DecodeFile(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0) return new DecodedFile(string.Empty, null);
        string text;
        using (var reader = new StreamReader(new MemoryStream(bytes), new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true))
            text = reader.ReadToEnd();
        if (text.Length > DocumentSizeLimit.MaxDocumentSizeChars || Encoding.UTF8.GetByteCount(text) > DocumentSizeLimit.MaxDocumentSizeChars)
            return new DecodedFile(null, "The file is larger than the 10 MB limit for a document.");
        return new DecodedFile(text, null);
    }
}
