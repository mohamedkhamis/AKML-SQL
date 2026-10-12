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
    /// A name the user typed (Save, Save as), made safe to save under: the characters a file name cannot hold removed,
    /// a trailing <c>.sql</c> dropped (the file gets one), spaces and dots trimmed; nothing left gives the next default
    /// name. Any other dot is part of the name: <c>Q3.2025 revenue</c> stays whole.
    /// </summary>
    public static string SanitizeFileName(string? name, int nextDefaultNumber)
    {
        var raw = (name ?? string.Empty).Trim();
        if (raw.EndsWith(SqlExtension, StringComparison.OrdinalIgnoreCase)) raw = raw.Substring(0, raw.Length - SqlExtension.Length);
        return Clean(raw, nextDefaultNumber);
    }

    /// <summary>
    /// The document name for an opened file: its extension dropped (<c>monthly.close.sql</c> → <c>monthly.close</c>),
    /// then made safe as <see cref="SanitizeFileName"/> does.
    /// </summary>
    public static string NameFromFile(string? fileName, int nextDefaultNumber)
    {
        var raw = (fileName ?? string.Empty).Trim();
        int dot = raw.LastIndexOf('.');
        if (dot > 0) raw = raw.Substring(0, dot);
        return Clean(raw, nextDefaultNumber);
    }

    private const string SqlExtension = ".sql";

    private static string Clean(string raw, int nextDefaultNumber)
    {
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
    /// Read a file: UTF-8 or UTF-16 (LE or BE) with a BOM; without one, UTF-8 when the bytes are valid UTF-8, else
    /// Windows-1252 — the ANSI code page SSMS saves in, so a legacy script's <c>Café</c> or <c>£</c> reads as written
    /// instead of as replacement characters. Refused above the 10 MB limit — counted in characters and in UTF-8 bytes,
    /// since the document travels to the engine as UTF-8.
    /// </summary>
    public static DecodedFile DecodeFile(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0) return new DecodedFile(string.Empty, null);
        string text;
        if (HasBom(bytes))
        {
            using var reader = new StreamReader(new MemoryStream(bytes), new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
            text = reader.ReadToEnd();
        }
        else
        {
            try { text = StrictUtf8.GetString(bytes); }
            catch (DecoderFallbackException) { text = DecodeWindows1252(bytes); }
        }
        if (text.Length > DocumentSizeLimit.MaxDocumentSizeChars || Encoding.UTF8.GetByteCount(text) > DocumentSizeLimit.MaxDocumentSizeChars)
            return new DecodedFile(null, "The file is larger than the 10 MB limit for a document.");
        return new DecodedFile(text, null);
    }

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static bool HasBom(byte[] b) =>
        (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF)
        || (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE)
        || (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF);

    // Windows-1252 differs from Latin-1 only in 0x80–0x9F. Decoded by table: the code-pages provider is not in the
    // browser runtime. The five unassigned bytes map to the C1 controls, as Windows' MultiByteToWideChar does.
    private static readonly char[] Cp1252High =
    {
        '\u20AC', '\u0081', '\u201A', '\u0192', '\u201E', '\u2026', '\u2020', '\u2021',
        '\u02C6', '\u2030', '\u0160', '\u2039', '\u0152', '\u008D', '\u017D', '\u008F',
        '\u0090', '\u2018', '\u2019', '\u201C', '\u201D', '\u2022', '\u2013', '\u2014',
        '\u02DC', '\u2122', '\u0161', '\u203A', '\u0153', '\u009D', '\u017E', '\u0178',
    };

    internal static string DecodeWindows1252(byte[] bytes)
    {
        var chars = new char[bytes.Length];
        for (int i = 0; i < bytes.Length; i++)
        {
            byte b = bytes[i];
            chars[i] = b >= 0x80 && b <= 0x9F ? Cp1252High[b - 0x80] : (char)b;
        }
        return new string(chars);
    }
}
