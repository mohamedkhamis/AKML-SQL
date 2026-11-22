#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Utilities;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 047 (T071) — the smallest text buffer an editor-side controller can be built on in a plain test: fixed text,
    /// a property bag, and the <see cref="Changed"/> event. Editing members are not supported; an analysis controller
    /// only reads the text and listens for edits.
    /// </summary>
    internal sealed class FakeTextBuffer : ITextBuffer, ITextSnapshot
    {
        private readonly string _text;

        public FakeTextBuffer(string text) => _text = text ?? string.Empty;

        public PropertyCollection Properties { get; } = new PropertyCollection();

        public ITextSnapshot CurrentSnapshot => this;

        // Raised by nothing here: the deferral tests prove that analysis runs without an edit.
#pragma warning disable CS0067
        public event EventHandler<TextContentChangedEventArgs>? Changed;
        public event EventHandler<TextContentChangedEventArgs>? ChangedLowPriority;
        public event EventHandler<TextContentChangedEventArgs>? ChangedHighPriority;
        public event EventHandler<TextContentChangingEventArgs>? Changing;
        public event EventHandler? PostChanged;
        public event EventHandler<ContentTypeChangedEventArgs>? ContentTypeChanged;
        public event EventHandler<SnapshotSpanEventArgs>? ReadOnlyRegionsChanged;
#pragma warning restore CS0067

        // ── ITextSnapshot ────────────────────────────────────────────────────────────────────────────────────────
        public ITextBuffer TextBuffer => this;
        public int Length => _text.Length;
        public char this[int position] => _text[position];
        public string GetText() => _text;
        public string GetText(int startIndex, int length) => _text.Substring(startIndex, length);
        public string GetText(Span span) => _text.Substring(span.Start, span.Length);
        public char[] ToCharArray(int startIndex, int length) => _text.ToCharArray(startIndex, length);
        public void CopyTo(int sourceIndex, char[] destination, int destinationIndex, int count) =>
            _text.CopyTo(sourceIndex, destination, destinationIndex, count);
        public void Write(TextWriter writer) => writer.Write(_text);
        public void Write(TextWriter writer, Span span) => writer.Write(GetText(span));

        public IContentType ContentType => throw new NotSupportedException();
        public ITextVersion Version => throw new NotSupportedException();
        public int LineCount => throw new NotSupportedException();
        public IEnumerable<ITextSnapshotLine> Lines => throw new NotSupportedException();
        public ITrackingPoint CreateTrackingPoint(int position, PointTrackingMode trackingMode) => throw new NotSupportedException();
        public ITrackingPoint CreateTrackingPoint(int position, PointTrackingMode trackingMode, TrackingFidelityMode trackingFidelity) => throw new NotSupportedException();
        public ITrackingSpan CreateTrackingSpan(Span span, SpanTrackingMode trackingMode) => throw new NotSupportedException();
        public ITrackingSpan CreateTrackingSpan(Span span, SpanTrackingMode trackingMode, TrackingFidelityMode trackingFidelity) => throw new NotSupportedException();
        public ITrackingSpan CreateTrackingSpan(int start, int length, SpanTrackingMode trackingMode) => throw new NotSupportedException();
        public ITrackingSpan CreateTrackingSpan(int start, int length, SpanTrackingMode trackingMode, TrackingFidelityMode trackingFidelity) => throw new NotSupportedException();
        public ITextSnapshotLine GetLineFromLineNumber(int lineNumber) => throw new NotSupportedException();
        public ITextSnapshotLine GetLineFromPosition(int position) => throw new NotSupportedException();
        public int GetLineNumberFromPosition(int position) => throw new NotSupportedException();

        // ── ITextBuffer editing (not supported) ──────────────────────────────────────────────────────────────────
        public bool EditInProgress => false;
        public ITextEdit CreateEdit(EditOptions options, int? reiteratedVersionNumber, object editTag) => throw new NotSupportedException();
        public ITextEdit CreateEdit() => throw new NotSupportedException();
        public IReadOnlyRegionEdit CreateReadOnlyRegionEdit() => throw new NotSupportedException();
        public void TakeThreadOwnership() { }
        public bool CheckEditAccess() => true;
        public void ChangeContentType(IContentType newContentType, object editTag) => throw new NotSupportedException();
        public ITextSnapshot Insert(int position, string text) => throw new NotSupportedException();
        public ITextSnapshot Delete(Span deleteSpan) => throw new NotSupportedException();
        public ITextSnapshot Replace(Span replaceSpan, string replaceWith) => throw new NotSupportedException();
        public bool IsReadOnly(int position) => false;
        public bool IsReadOnly(int position, bool isEdit) => false;
        public bool IsReadOnly(Span span) => false;
        public bool IsReadOnly(Span span, bool isEdit) => false;
        public NormalizedSpanCollection GetReadOnlyExtents(Span span) => new NormalizedSpanCollection();
    }
}
