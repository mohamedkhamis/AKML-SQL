using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AkmlSql.Core.Update;
using AkmlSql.Updater;
using Xunit;

namespace AkmlSql.Installer.Tests;

/// <summary>
/// Spec 036 US5 — <c>AkmlSql.Updater --download</c> (FR-039/FR-039a/FR-040, contract
/// <c>update-manifest.md</c> §3). Drives <see cref="UpdateDownloader"/> in-proc with a stub
/// <see cref="HttpMessageHandler"/> and temp paths:
/// a checksum mismatch deletes the file, exits 2 and records the reason; a cancelled download
/// leaves no <c>.partial</c> on disk and returns the offer to the available state; every
/// result-file write stays atomic.
/// </summary>
public sealed class UpdateDownloaderTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly string _root;
    private readonly string _resultPath;
    private readonly string _cacheDir;

    public UpdateDownloaderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "akml-download-" + Guid.NewGuid().ToString("N"));
        _resultPath = Path.Combine(_root, "state", "update-available.json");
        _cacheDir = Path.Combine(_root, "cache");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task No_result_file_nothing_to_do_exit0()
    {
        var handler = new StubHandler((_, _) => throw new InvalidOperationException("no HTTP expected"));

        var exit = await new UpdateDownloader(handler, _resultPath, _cacheDir).RunAsync();

        Assert.Equal(0, exit);
        Assert.False(File.Exists(_resultPath));
    }

    [Fact]
    public async Task Happy_path_verifies_hash_and_records_verified_installer()
    {
        var bytes = Encoding.ASCII.GetBytes("fake installer payload");
        SeedResult(NewResult(), Sha256Hex(bytes));
        var handler = new StubHandler((_, _) => Task.FromResult(Respond(bytes)));

        var exit = await new UpdateDownloader(handler, _resultPath, _cacheDir).RunAsync();

        Assert.Equal(0, exit);
        var final = Path.Combine(_cacheDir, "AKMLSQLSetup-1.26.0903.0900.exe");
        Assert.True(File.Exists(final));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(final));

        var persisted = ReadResult();
        Assert.Equal("verified", persisted.DownloadState);
        Assert.Null(persisted.FailureReason);
        Assert.Equal(Path.GetFullPath(final), persisted.VerifiedInstallerPath);
        Assert.True(Path.IsPathRooted(persisted.VerifiedInstallerPath!));

        // Atomic writes + rename: no temp or partial debris anywhere.
        Assert.Empty(Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(_root, "*.partial", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Checksum_mismatch_deletes_file_exit2_sets_failure_reason()
    {
        var bytes = Encoding.ASCII.GetBytes("fake installer payload");
        // The manifest hash names DIFFERENT bytes than the server sends.
        SeedResult(NewResult(), Sha256Hex(Encoding.ASCII.GetBytes("the real installer")));
        var handler = new StubHandler((_, _) => Task.FromResult(Respond(bytes)));

        var exit = await new UpdateDownloader(handler, _resultPath, _cacheDir).RunAsync();

        Assert.Equal(2, exit); // FR-040: verification failure aborts the flow
        Assert.Empty(Directory.Exists(_cacheDir)
            ? Directory.GetFiles(_cacheDir, "*.exe*")
            : Array.Empty<string>());

        var persisted = ReadResult();
        Assert.Equal("failed", persisted.DownloadState);
        Assert.Equal("checksum mismatch", persisted.FailureReason);
        Assert.Null(persisted.VerifiedInstallerPath);
        Assert.Empty(Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Cancelled_download_leaves_no_partial_and_keeps_the_offer()
    {
        var bytes = Encoding.ASCII.GetBytes("fake installer payload");
        SeedResult(NewResult(), Sha256Hex(bytes));
        // First reads deliver bytes, then the stream hangs until the token cancels —
        // the downloader is mid-copy with a .partial on disk when cancellation lands.
        var handler = new StubHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new HangingReadStream()) }));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        var exit = await new UpdateDownloader(handler, _resultPath, _cacheDir).RunAsync(cts.Token);

        Assert.Equal(0, exit); // cancel is not a verification failure
        Assert.Empty(Directory.GetFiles(_root, "*.partial", SearchOption.AllDirectories));

        var persisted = ReadResult();
        Assert.True(persisted.Available); // offer retained (state machine: downloading -> available)
        Assert.Equal("none", persisted.DownloadState);
        Assert.Null(persisted.FailureReason);
    }

    [Fact]
    public async Task Already_verified_short_circuits_without_http()
    {
        var bytes = Encoding.ASCII.GetBytes("fake installer payload");
        Directory.CreateDirectory(_cacheDir);
        var final = Path.Combine(_cacheDir, "AKMLSQLSetup-1.26.0903.0900.exe");
        await File.WriteAllBytesAsync(final, bytes);
        var result = NewResult();
        result.DownloadState = "verified";
        result.VerifiedInstallerPath = final;
        SeedResult(result, Sha256Hex(bytes));

        var handler = new StubHandler((_, _) => throw new InvalidOperationException("no HTTP expected"));

        var exit = await new UpdateDownloader(handler, _resultPath, _cacheDir).RunAsync();

        Assert.Equal(0, exit);
        var persisted = ReadResult();
        Assert.Equal("verified", persisted.DownloadState);
    }

    [Fact]
    public async Task Non_https_download_url_rejected_before_any_request()
    {
        var result = NewResult();
        result.DownloadUrl = "http://downloads.example.com/AKMLSQLSetup.exe";
        SeedResult(result, Sha256Hex(Encoding.ASCII.GetBytes("x")));
        var calls = 0;
        var handler = new StubHandler((_, _) => { calls++; return Task.FromResult(Respond(new byte[1])); });

        var exit = await new UpdateDownloader(handler, _resultPath, _cacheDir).RunAsync();

        Assert.Equal(2, exit);
        Assert.Equal(0, calls); // rejected before the request
        var persisted = ReadResult();
        Assert.Equal("failed", persisted.DownloadState);
        Assert.Contains("HTTPS", persisted.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    // --- helpers -----------------------------------------------------------

    private static UpdateResult NewResult() => new()
    {
        Available = true,
        Version = "1.26.0903.0900",
        DownloadUrl = "https://github.com/mohamedkhamis/AKML-SQL/releases/download/v1.26.0903.0900/AKMLSQLSetup-1.26.0903.0900.exe",
        ReleaseNotesUrl = "https://github.com/mohamedkhamis/AKML-SQL/releases",
        CheckedAt = DateTimeOffset.UtcNow
    };

    private void SeedResult(UpdateResult result, string sha256)
    {
        result.Sha256Hash = sha256;
        Directory.CreateDirectory(Path.GetDirectoryName(_resultPath)!);
        File.WriteAllText(_resultPath, JsonSerializer.Serialize(result, JsonOptions));
    }

    private UpdateResult ReadResult() =>
        JsonSerializer.Deserialize<UpdateResult>(File.ReadAllText(_resultPath), JsonOptions)!;

    private static HttpResponseMessage Respond(byte[] bytes) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    private static string Sha256Hex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) =>
            _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            _respond(request, cancellationToken);
    }

    /// <summary>Delivers one buffer of bytes, then hangs until the copy's token cancels.</summary>
    private sealed class HangingReadStream : Stream
    {
        private bool _delivered;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_delivered)
            {
                _delivered = true;
                return Math.Min(buffer.Length, 4096);
            }

            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }
}
