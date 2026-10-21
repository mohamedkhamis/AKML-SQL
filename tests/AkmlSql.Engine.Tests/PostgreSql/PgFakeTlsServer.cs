using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace AkmlSql.Engine.Tests.PostgreSql;

/// <summary>
/// Spec 042 (FR-101) test support: certificates made here (authorities, intermediates, server certificates with any validity, names and subject)
/// and a PostgreSQL server that only knows how to answer the SSLRequest and complete a TLS handshake, recording what the client sent.
/// Everything is removed when the fixture is disposed.
/// </summary>
internal sealed class PgTlsFixtures : IDisposable
{
    private readonly List<IDisposable> _cleanup = new();

    public sealed record Authority(X509Certificate2 Certificate, RSA Key, string PemPath, string Name);

    /// <summary>A self-signed authority, with its certificate (PEM) written to a file a connection string can name.</summary>
    public Authority NewAuthority(string name = "Test Root", int notBeforeDays = -30, int notAfterDays = 5 * 365)
    {
        var key = RSA.Create(2048);
        var unique = Unique(name);
        var request = new CertificateRequest(Name(unique), key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(notBeforeDays), DateTimeOffset.UtcNow.AddDays(notAfterDays));
        _cleanup.Add(certificate);
        _cleanup.Add(key);
        return new Authority(certificate, key, WriteText(".crt", certificate.ExportCertificatePem()), unique);
    }

    /// <summary>An authority signed by <paramref name="root"/>; its validity may lie outside the root's, which the issuer-based overload would refuse.</summary>
    public Authority NewIntermediate(Authority root, string name = "Test Intermediate", int notBeforeDays = -30, int notAfterDays = 4 * 365)
    {
        var key = RSA.Create(2048);
        var unique = Unique(name);
        var request = new CertificateRequest(Name(unique), key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        using var issued = request.Create(root.Certificate.SubjectName, Generator(root), DateTimeOffset.UtcNow.AddDays(notBeforeDays), DateTimeOffset.UtcNow.AddDays(notAfterDays), Serial());
        var withKey = issued.CopyWithPrivateKey(key);
        _cleanup.Add(withKey);
        _cleanup.Add(key);
        return new Authority(withKey, key, WriteText(".crt", issued.ExportCertificatePem()), unique);
    }

    /// <param name="commonName">The subject's common name when it is not the first DNS name.</param>
    /// <param name="withoutNames">Leaves out the subject alternative names, so the certificate says who it is only in its subject.</param>
    /// <param name="rawNames">A subject alternative name extension body to use as given (for a malformed one).</param>
    public X509Certificate2 NewServerCertificate(Authority issuer, string[] dnsNames, string[]? addresses = null, int notBeforeDays = -1, int notAfterDays = 30,
        string? commonName = null, bool withoutNames = false, byte[]? rawNames = null)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(Name(commonName ?? dnsNames[0]), key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        if (rawNames is not null)
        {
            request.CertificateExtensions.Add(new X509Extension(new Oid("2.5.29.17"), rawNames, false));
        }
        else if (!withoutNames)
        {
            var names = new SubjectAlternativeNameBuilder();
            foreach (var dns in dnsNames) names.AddDnsName(dns);
            foreach (var address in addresses ?? Array.Empty<string>()) names.AddIpAddress(IPAddress.Parse(address));
            request.CertificateExtensions.Add(names.Build());
        }
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));

        using var issued = request.Create(issuer.Certificate.SubjectName, Generator(issuer), DateTimeOffset.UtcNow.AddDays(notBeforeDays), DateTimeOffset.UtcNow.AddDays(notAfterDays), Serial());
        using var withKey = issued.CopyWithPrivateKey(key);
        // SslStream on Windows needs a key that is persisted with the certificate, which a PKCS#12 round trip gives.
        var loaded = X509CertificateLoader.LoadPkcs12(withKey.Export(X509ContentType.Pfx, "x"), "x", X509KeyStorageFlags.Exportable);
        _cleanup.Add(loaded);
        return loaded;
    }

    /// <summary>A file in the temporary folder that is removed with the fixture.</summary>
    public string WriteText(string extension, string text)
    {
        var path = Path.Combine(Path.GetTempPath(), "akml-tls-" + Guid.NewGuid().ToString("N") + extension);
        File.WriteAllText(path, text);
        _cleanup.Add(new Deleter(path));
        return path;
    }

    public string WriteBytes(string extension, byte[] content)
    {
        var path = Path.Combine(Path.GetTempPath(), "akml-tls-" + Guid.NewGuid().ToString("N") + extension);
        File.WriteAllBytes(path, content);
        _cleanup.Add(new Deleter(path));
        return path;
    }

    /// <summary>A server that presents <paramref name="certificate"/> followed by <paramref name="sentWith"/>; a null certificate makes it answer that it does not do TLS.</summary>
    public PgFakeTlsServer Serve(X509Certificate2? certificate, params X509Certificate2[] sentWith)
    {
        var server = new PgFakeTlsServer(certificate, sentWith);
        _cleanup.Add(server);
        return server;
    }

    /// <summary>A port nothing listens on.</summary>
    public static int ClosedPort()
    {
        var closed = new TcpListener(IPAddress.Loopback, 0);
        closed.Start();
        var port = ((IPEndPoint)closed.LocalEndpoint).Port;
        closed.Stop();
        return port;
    }

    public void Dispose()
    {
        foreach (var item in _cleanup) item.Dispose();
    }

    // Windows remembers certificates it has seen by subject name, so two authorities of one name made by different tests would be taken for each other
    // (the chain then fails its signature check instead of reporting the issuer as missing). Every authority therefore gets a name of its own.
    private static string Unique(string name) => $"{name} {Guid.NewGuid():N}"[..(name.Length + 9)];

    private static X500DistinguishedName Name(string commonName)
    {
        var builder = new X500DistinguishedNameBuilder();
        builder.AddCommonName(commonName);
        return builder.Build();
    }

    private static X509SignatureGenerator Generator(Authority issuer) => X509SignatureGenerator.CreateForRSA(issuer.Key, RSASignaturePadding.Pkcs1);

    private static byte[] Serial()
    {
        var serial = new byte[8];
        RandomNumberGenerator.Fill(serial);
        serial[0] &= 0x7F;
        return serial;
    }

    private sealed class Deleter : IDisposable
    {
        private readonly string _path;
        public Deleter(string path) => _path = path;

        public void Dispose()
        {
            try { File.Delete(_path); } catch { /* a leftover temporary file is harmless */ }
        }
    }
}

/// <summary>What one client connection did: the eight bytes it opened with, whether the TLS handshake completed, and how many bytes it sent after that.</summary>
internal sealed record PgClientSession(byte[] Request, bool Handshook, int BytesAfterHandshake);

internal sealed class PgFakeTlsServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly SslStreamCertificateContext? _context;
    private readonly List<PgClientSession> _sessions = new();
    private readonly SemaphoreSlim _finished = new(0);
    private int _accepted;
    private int _reported;

    public PgFakeTlsServer(X509Certificate2? certificate, X509Certificate2[] sentWith)
    {
        if (certificate is not null)
        {
            _context = SslStreamCertificateContext.Create(certificate, sentWith.Length == 0 ? null : new X509Certificate2Collection(sentWith), offline: true);
        }
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = Task.Run(Loop);
    }

    public int Port { get; }

    /// <summary>How many connections the server has accepted so far.</summary>
    public int Accepted => Volatile.Read(ref _accepted);

    /// <summary>The next client session that has ended (the client hung up or the handshake failed).</summary>
    public async Task<PgClientSession> NextSessionAsync(int seconds = 10)
    {
        if (!await _finished.WaitAsync(TimeSpan.FromSeconds(seconds))) throw new TimeoutException("No client session ended.");
        lock (_sessions) return _sessions[_reported++];
    }

    private async Task Loop()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                Interlocked.Increment(ref _accepted);
                _ = Task.Run(() => ServeAsync(client));
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    private async Task ServeAsync(TcpClient client)
    {
        var request = new byte[8];
        var handshook = false;
        var after = 0;
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var read = 0;
                while (read < 8)
                {
                    var n = await stream.ReadAsync(request.AsMemory(read), _stop.Token);
                    if (n == 0) return;
                    read += n;
                }
                if (_context is null)
                {
                    await stream.WriteAsync(new[] { (byte)'N' }, _stop.Token);
                    return;
                }
                await stream.WriteAsync(new[] { (byte)'S' }, _stop.Token);
                using var ssl = new SslStream(stream);
                await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificateContext = _context, ClientCertificateRequired = false }, _stop.Token);
                handshook = true;
                var buffer = new byte[256];
                int count;
                while ((count = await ssl.ReadAsync(buffer, _stop.Token)) > 0) after += count;       // until the client hangs up
            }
            catch (Exception ex) when (ex is IOException or AuthenticationException or OperationCanceledException or ObjectDisposedException or SocketException)
            {
                // the client hung up, as the diagnosis and a refusing driver do
            }
            finally
            {
                lock (_sessions) _sessions.Add(new PgClientSession(request, handshook, after));
                _finished.Release();
            }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        _stop.Dispose();
    }
}
