using System.Globalization;
using System.Net;
using System.Text;
using Serilog;

namespace AkmlSql.Engine.Transports;

/// <summary>Read-only public bundle on the bridge listener; private state is never in its manifest.</summary>
internal sealed class StaticWebResponder
{
    public const string ContentSecurityPolicy = "default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; " +
        "style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self' " +
        "ws://127.0.0.1:* wss://127.0.0.1:* ws://localhost:* wss://localhost:* wss: " +
        "https://api.openai.com https://api.anthropic.com https://generativelanguage.googleapis.com " +
        "https://*.openai.azure.com http://localhost:11434 http://127.0.0.1:11434 http://localhost:1234 http://127.0.0.1:1234;";

    private static readonly IReadOnlyDictionary<string, string> ContentTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8", [".js"] = "text/javascript", [".css"] = "text/css",
        [".wasm"] = "application/wasm", [".json"] = "application/json", [".map"] = "application/json",
        [".svg"] = "image/svg+xml", [".txt"] = "text/plain; charset=utf-8", [".sql"] = "text/plain; charset=utf-8",
        [".pdb"] = "application/octet-stream",
    };
    private static readonly HashSet<string> StateFiles = new(StringComparer.OrdinalIgnoreCase)
        { "pairing-pin.txt", "tokens.json", "config.json", "INSTALL-SUMMARY.txt" };
    private static readonly object LogGate = new();
    private static DateTime _lastRefusal, _lastBusy;
    private static int _suppressed;
    private readonly string _root;
    private readonly Dictionary<string, Entry> _manifest = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _slots = new(32);

    private sealed record Entry(string FullPath, long Length, DateTime LastWriteUtc, string ContentType)
    {
        internal DateTime LastModified => new(LastWriteUtc.Ticks - LastWriteUtc.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
        internal string ETag => $"\"{Length:x}-{LastWriteUtc.Ticks:x}\"";
    }

    private StaticWebResponder(string root)
    {
        _root = root;
        Walk(new DirectoryInfo(root));
        if (!_manifest.ContainsKey("index.html")) throw new InvalidDataException("No public index.html in the web root.");
    }

    internal static StaticWebResponder? Create(WebSocketTransportOptions options)
    {
        if (string.IsNullOrEmpty(options.WebRoot)) return null;
        try
        {
            if (!Path.IsPathFullyQualified(options.WebRoot)) throw new InvalidDataException("The path must be absolute.");
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.WebRoot));
            if (string.Equals(root, Path.TrimEndingDirectorySeparator(Path.GetPathRoot(root)!), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("A volume root cannot be served.");
            foreach (var stateFile in new[] { options.ConfigFilePath, options.TokenStorePath })
            {
                if (string.IsNullOrWhiteSpace(stateFile)) continue;
                var state = Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(Path.GetFullPath(stateFile))!);
                if (Contains(root, state) || Contains(state, root)) throw new InvalidDataException("The web root overlaps private engine state.");
            }
            if (!Directory.Exists(root)) throw new InvalidDataException("The folder does not exist.");
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("A reparse-point root cannot be served.");
            var responder = new StaticWebResponder(root);
            Log.Information("WebSocketTransport: serving the web edition from {WebRoot} ({Count} files)", root, responder._manifest.Count);
            return responder;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Log.Warning("WebSocketTransport: not serving the web edition from {WebRoot}: {Reason}", options.WebRoot, ex.Message);
            return null;
        }
    }

    private static bool Contains(string parent, string child) =>
        child.Equals(parent, StringComparison.OrdinalIgnoreCase) ||
        child.StartsWith(Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private void Walk(DirectoryInfo directory)
    {
        foreach (var item in directory.EnumerateFileSystemInfos())
        {
            if ((item.Attributes & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint)) != 0) continue;
            if (item is DirectoryInfo folder) { Walk(folder); continue; }
            if (item is not FileInfo file || !ContentTypes.TryGetValue(file.Extension, out var type) || StateFiles.Contains(file.Name)) continue;
            var path = Path.GetFullPath(file.FullName);
            if (!Contains(_root, path)) throw new InvalidDataException("A manifest entry lies outside the web root.");
            _manifest.Add(Path.GetRelativePath(_root, path).Replace(Path.DirectorySeparatorChar, '/'),
                new Entry(path, file.Length, file.LastWriteTimeUtc, type));
        }
    }

    internal async Task RespondAsync(HttpListenerContext context)
    {
        try
        {
            var path = Uri.UnescapeDataString(context.Request.Url!.AbsolutePath);
            if (!TryKey(path, out var key))
            {
                await RefuseAsync(context, 400, "Bad request.").ConfigureAwait(false);
                return;
            }
            if (!_manifest.TryGetValue(key, out var entry))
            {
                if (key[(key.LastIndexOf('/') + 1)..].Contains('.'))
                {
                    await ErrorAsync(context, 404, "Not found.").ConfigureAwait(false);
                    return;
                }
                entry = _manifest["index.html"];
            }
            SetHeaders(context.Response);
            context.Response.ContentType = entry.ContentType;
            context.Response.Headers["ETag"] = entry.ETag;
            context.Response.Headers["Last-Modified"] = entry.LastModified.ToString("R", CultureInfo.InvariantCulture);
            if (NotModified(context.Request, entry))
            {
                context.Response.StatusCode = 304;
                context.Response.Close();
                return;
            }
            if (context.Request.HttpMethod == "HEAD")
            {
                context.Response.ContentLength64 = entry.Length;
                context.Response.Close();
                return;
            }
            if (!await _slots.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false))
            {
                lock (LogGate)
                {
                    if (DateTime.UtcNow - _lastBusy >= TimeSpan.FromMinutes(1))
                    {
                        _lastBusy = DateTime.UtcNow;
                        Log.Warning("WebSocketTransport: static web response slots are busy.");
                    }
                }
                context.Response.Headers["Retry-After"] = "5";
                await ErrorAsync(context, 503, "Busy.").ConfigureAwait(false);
                return;
            }
            try
            {
                var fullPath = Path.GetFullPath(entry.FullPath);
                if (!fullPath.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    await RefuseAsync(context, 400, "Bad request.").ConfigureAwait(false);
                    return;
                }
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60 + entry.Length / 65536d));
                using var abort = deadline.Token.Register(() => Abort(context.Response));
                await using var file = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
                context.Response.ContentLength64 = entry.Length;
                await file.CopyToAsync(context.Response.OutputStream, deadline.Token).ConfigureAwait(false);
                context.Response.Close();
            }
            finally { _slots.Release(); }
        }
        catch { Abort(context.Response); }
    }

    private static bool TryKey(string path, out string key)
    {
        key = string.Empty;
        if (path.Any(c => c < 0x20 || c == 0x7f || c is '\\' or ':' or '%' or '~')) return false;
        if (path == "/") { key = "index.html"; return true; }
        if (!path.StartsWith('/')) return false;
        key = path[1..];
        return key.Split('/').All(part => part.Length > 0 && part is not "." and not ".." && !part.EndsWith('.') && !part.EndsWith(' '));
    }

    private static bool NotModified(HttpListenerRequest request, Entry entry)
    {
        var etag = request.Headers["If-None-Match"];
        if (etag != null) return etag.Split(',').Any(value => value.Trim() is "*" || value.Trim().TrimStart('W', '/') == entry.ETag);
        return DateTimeOffset.TryParse(request.Headers["If-Modified-Since"], CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var since) && since.UtcDateTime >= entry.LastModified;
    }

    internal static void SetHeaders(HttpListenerResponse response)
    {
        response.Headers["Cache-Control"] = "no-cache";
        response.Headers["Content-Security-Policy"] = ContentSecurityPolicy;
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["X-AKML-Host"] = "engine";
    }

    internal static async Task RefuseAsync(HttpListenerContext context, int status, string body)
    {
        lock (LogGate)
        {
            var now = DateTime.UtcNow;
            if (now - _lastRefusal >= TimeSpan.FromSeconds(1))
            {
                if (_suppressed > 0) Log.Warning("WebSocketTransport: {Count} refused requests not logged", _suppressed);
                _suppressed = 0;
                _lastRefusal = now;
                var path = context.Request.Url?.AbsolutePath ?? string.Empty;
                Log.Warning("WebSocketTransport: refused {Method} {Path} from {Remote} ({Reason})",
                    context.Request.HttpMethod, path[..Math.Min(path.Length, 200)], context.Request.RemoteEndPoint, body);
            }
            else _suppressed++;
        }
        await ErrorAsync(context, status, body).ConfigureAwait(false);
    }

    internal static async Task ErrorAsync(HttpListenerContext context, int status, string body)
    {
        try
        {
            SetHeaders(context.Response);
            context.Response.StatusCode = status;
            context.Response.Headers.Remove("ETag");
            context.Response.Headers.Remove("Last-Modified");
            context.Response.ContentType = "text/plain; charset=utf-8";
            var bytes = Encoding.UTF8.GetBytes(body);
            context.Response.ContentLength64 = bytes.Length;
            if (context.Request.HttpMethod != "HEAD") await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
            context.Response.Close();
        }
        catch { Abort(context.Response); }
    }

    private static void Abort(HttpListenerResponse response)
    {
        try { response.Abort(); } catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or IOException) { }
    }
}
