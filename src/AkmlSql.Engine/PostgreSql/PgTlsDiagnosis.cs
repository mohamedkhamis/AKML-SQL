using System.Globalization;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Npgsql;

namespace AkmlSql.Engine.PostgreSql;

/// <summary>
/// Spec 042 (FR-101) - says why a verifying TLS mode refused a server's certificate. The driver reports every certificate refusal with one
/// sentence ("the remote certificate was rejected by the provided RemoteCertificateValidationCallback"), which is the same whether the certificate
/// is from an authority the client does not know, has expired, or was issued for other host names, and the person who must act needs to know which.
///
/// <para>
/// The diagnosis repeats the start of the connection once, only after a refusal: it asks the server for TLS exactly as PostgreSQL clients do (the
/// eight-byte SSLRequest, answered by one byte), completes the handshake while recording what the server presented without trusting it, and then
/// judges the certificate the way the mode would: the chain against the root certificate in force (the one in the connection string, else
/// <c>PGSSLROOTCERT</c>, else <c>%APPDATA%\postgresql\root.crt</c>, as the driver does; this machine's roots when there is none) and, for
/// <c>verify-full</c>, the name. No password and no startup message is sent, so nothing secret leaves the engine, and the answer is never used to
/// accept a connection; it only words the refusal. Whatever the server put in its certificate is shown cleaned and shortened, because a server that
/// fails verification is exactly the one that may be lying.
/// </para>
/// </summary>
public static class PgTlsDiagnosis
{
    /// <summary>The PostgreSQL SSLRequest message: length 8, then the code 80877103.</summary>
    internal static readonly byte[] SslRequest = { 0x00, 0x00, 0x00, 0x08, 0x04, 0xD2, 0x16, 0x2F };

    private static readonly TimeSpan DefaultLimit = TimeSpan.FromSeconds(5);
    private const int DisplayLength = 64;
    private const int NamesListed = 10;

    /// <summary>
    /// The specific reason the server's certificate was refused, with what to do about it; null when the mode does not verify the certificate, the
    /// server cannot be reached again, does not offer TLS, a root certificate in force cannot be read, or the certificate turns out acceptable (the
    /// refusal then had another cause).
    /// </summary>
    public static Task<string?> ExplainAsync(string connectionString, CancellationToken ct) => ExplainAsync(connectionString, DefaultLimit, ct);

    internal static async Task<string?> ExplainAsync(string connectionString, TimeSpan limit, CancellationToken ct)
    {
        NpgsqlConnectionStringBuilder builder;
        try { builder = new NpgsqlConnectionStringBuilder(connectionString); }
        catch (ArgumentException) { return null; }

        if (builder.SslMode is not (SslMode.VerifyCA or SslMode.VerifyFull)) return null;
        var host = (builder.Host ?? string.Empty).Split(',')[0].Trim();
        if (host.Length == 0 || host.StartsWith('/')) return null;          // empty, or a Unix-domain socket directory: there is no certificate to look at

        using var timeLimit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeLimit.CancelAfter(limit);

        X509Certificate2? presented = null;
        var presentedChain = new X509Certificate2Collection();
        try
        {
            var policyErrors = SslPolicyErrors.None;
            using (var tcp = new TcpClient())
            {
                await tcp.ConnectAsync(host, builder.Port, timeLimit.Token).ConfigureAwait(false);
                var stream = tcp.GetStream();

                await stream.WriteAsync(SslRequest, timeLimit.Token).ConfigureAwait(false);
                var answer = new byte[1];
                if (await stream.ReadAsync(answer, timeLimit.Token).ConfigureAwait(false) != 1 || answer[0] != (byte)'S') return null;

                using var ssl = new SslStream(stream, leaveInnerStreamOpen: false);
                await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = host,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                    // Record, and accept everything: this handshake carries no data and decides nothing.
                    RemoteCertificateValidationCallback = (_, certificate, chain, errors) =>
                    {
                        policyErrors = errors;
                        if (certificate is not null) presented = new X509Certificate2(certificate);
                        if (chain is not null)
                        {
                            foreach (var element in chain.ChainElements) presentedChain.Add(new X509Certificate2(element.Certificate));
                        }
                        return true;
                    },
                }, timeLimit.Token).ConfigureAwait(false);
            }

            return presented is null ? null : Judge(presented, presentedChain, policyErrors, host, builder.SslMode == SslMode.VerifyFull, RootCertificatePath(builder.RootCertificate));
        }
        catch (Exception ex) when (ex is IOException or SocketException or System.Security.Authentication.AuthenticationException or OperationCanceledException or ObjectDisposedException)
        {
            if (ct.IsCancellationRequested) throw;
            return null;                                                       // cannot look again: the driver's own sentence stands
        }
        finally
        {
            presented?.Dispose();
            foreach (var certificate in presentedChain) certificate.Dispose();
        }
    }

    /// <summary>
    /// The root certificate file the driver validates against, in the order libpq and the driver use: the connection string's, else the
    /// <c>PGSSLROOTCERT</c> environment variable, else <c>%APPDATA%\postgresql\root.crt</c> when that file exists; null when none is in force and the
    /// machine's own roots apply.
    /// </summary>
    internal static string? RootCertificatePath(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        var fromEnvironment = Environment.GetEnvironmentVariable("PGSSLROOTCERT");
        if (!string.IsNullOrWhiteSpace(fromEnvironment)) return fromEnvironment;
        var appData = Environment.GetEnvironmentVariable("APPDATA");
        if (string.IsNullOrWhiteSpace(appData)) return null;
        var fallback = Path.Combine(appData, "postgresql", "root.crt");
        return File.Exists(fallback) ? fallback : null;
    }

    /// <summary>
    /// Words a failure to read the root certificate file: the driver reads it while it validates, so a missing, empty or malformed file reaches
    /// the caller as a file or cryptography error inside the handshake error and never as a refused certificate. Null when the failure is not that.
    /// </summary>
    internal static string? UnreadableRoot(Exception ex, string connectionString)
    {
        if (ex is not NpgsqlException || ex.InnerException is null) return null;
        var inner = ex.InnerException;
        if (inner is not (FileNotFoundException or DirectoryNotFoundException or UnauthorizedAccessException or CryptographicException or IOException)) return null;

        NpgsqlConnectionStringBuilder builder;
        try { builder = new NpgsqlConnectionStringBuilder(connectionString); }
        catch (ArgumentException) { return null; }

        var path = RootCertificatePath(builder.RootCertificate);
        if (path is null) return null;
        var file = Path.GetFileName(path);

        // A client certificate file fails in the same ways; say it is the root certificate only when the error names it, or when there is no other file it could be.
        var mentionsRoot = inner.Message.Contains(path, StringComparison.OrdinalIgnoreCase);
        var otherFiles = !string.IsNullOrWhiteSpace(builder.SslCertificate) || !string.IsNullOrWhiteSpace(builder.SslKey);
        if (!mentionsRoot && (otherFiles || builder.SslMode is not (SslMode.VerifyCA or SslMode.VerifyFull))) return null;

        return inner switch
        {
            FileNotFoundException or DirectoryNotFoundException => $"The root certificate file {file} was not found. Check the path.",
            UnauthorizedAccessException => $"The root certificate file {file} could not be opened: access is denied.",
            _ => $"The root certificate file {file} could not be read as a certificate. Check that it holds the root certificate in PEM form, or is a .pfx file.",
        };
    }

    // The driver's own loading: a .pfx is a PKCS#12 file; anything else is read as PEM, and a file that holds no PEM certificate is tried as PKCS#12.
    private static X509Certificate2Collection? LoadRoots(string path)
    {
        try
        {
            var roots = new X509Certificate2Collection();
            if (!path.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase)) roots.ImportFromPemFile(path);
            if (roots.Count == 0) roots.Add(X509CertificateLoader.LoadPkcs12FromFile(path, null));
            return roots;
        }
        catch (Exception ex) when (ex is IOException or CryptographicException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static string? Judge(X509Certificate2 certificate, X509Certificate2Collection extras, SslPolicyErrors policyErrors,
        string host, bool checkName, string? rootCertificatePath)
    {
        X509Certificate2Collection? roots = null;
        if (rootCertificatePath is not null)
        {
            roots = LoadRoots(rootCertificatePath);
            if (roots is null) return null;           // the driver could not read it either, and its own error stands; guessing would blame the wrong thing
        }

        try
        {
            var subject = Display(certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false));
            var issuer = Display(certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: true));
            var rootFile = rootCertificatePath is null ? null : Path.GetFileName(rootCertificatePath);

            // The chain, judged as the mode judges it: against the root certificate in force, or this machine's roots when there is none.
            using var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.ExtraStore.AddRange(extras);
            if (roots is not null)
            {
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.AddRange(roots);
            }
            var trusted = chain.Build(certificate);

            // Time first, and of the certificate that is outside its dates: an expired authority under a good server certificate is not the server certificate's fault.
            for (var i = 0; i < chain.ChainElements.Count; i++)
            {
                var element = chain.ChainElements[i];
                if (!element.ChainElementStatus.Any(s => s.Status.HasFlag(X509ChainStatusFlags.NotTimeValid))) continue;
                var isGivenRoot = roots is not null && roots.Any(r => string.Equals(r.Thumbprint, element.Certificate.Thumbprint, StringComparison.OrdinalIgnoreCase));
                return NotValidNow(element.Certificate, i == 0, isGivenRoot, rootFile);
            }

            if (!trusted)
            {
                var problems = chain.ChainStatus.Aggregate(X509ChainStatusFlags.NoError, (all, s) => all | s.Status);
                if (rootFile is not null && problems.HasFlag(X509ChainStatusFlags.PartialChain))
                {
                    // The chain stops at a certificate whose issuer is nowhere: not among the roots given, and not sent. Either the file is the wrong one, or
                    // the missing certificate is an intermediate authority that the server should have sent; the sentence offers both, since only the person knows.
                    var missing = Display(chain.ChainElements[chain.ChainElements.Count - 1].Certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: true));
                    return $"The server's certificate (for \"{subject}\") chains up to \"{missing}\", which the root certificate you gave ({rootFile}) does not hold. " +
                           $"Give the root certificate file that holds \"{missing}\", or, if that is an intermediate authority, ask the server's administrator to send it with the certificate.";
                }

                return rootFile is not null
                    ? $"The server's certificate (for \"{subject}\", issued by \"{issuer}\") does not chain to the root certificate you gave ({rootFile}). " +
                      "Give the root certificate that signed it."
                    : $"The server's certificate (for \"{subject}\", issued by \"{issuer}\") is not signed by a root this machine trusts. " +
                      "Give the root certificate that signed it, or use TLS mode require, which encrypts without checking who the server is.";
            }

            if (checkName && policyErrors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch))
            {
                var names = NamesIn(certificate);
                var list = names.Count == 0
                    ? $"\"{subject}\""
                    : string.Join(", ", names.Take(NamesListed)) + (names.Count > NamesListed ? $" and {names.Count - NamesListed} more" : string.Empty);
                return $"The server's certificate is not valid for the host name \"{Display(host)}\"; it names {list}. " +
                       "Connect with one of those names, or use TLS mode verify-ca, which checks the certificate but not the name.";
            }

            return null;
        }
        finally
        {
            if (roots is not null) foreach (var root in roots) root.Dispose();
        }
    }

    private static string NotValidNow(X509Certificate2 certificate, bool isServerCertificate, bool isGivenRoot, string? rootFile)
    {
        var name = Display(certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false));
        var when = certificate.NotAfter < DateTime.Now
            ? $"it expired on {certificate.NotAfter:yyyy-MM-dd}"
            : $"it is not valid until {certificate.NotBefore:yyyy-MM-dd}";

        if (isServerCertificate)
            return $"The server's certificate (for \"{name}\") is not valid now: {when}. Ask the server's administrator for a current certificate.";
        if (isGivenRoot)
            return $"The root certificate you gave ({rootFile}, for \"{name}\") is not valid now: {when}. Give a current root certificate.";
        return $"The certificate \"{name}\" in the server's chain is not valid now: {when}. Ask the server's administrator for a current certificate chain.";
    }

    /// <summary>The DNS names and IP addresses a certificate says it is for (its subject alternative names), cleaned for display.</summary>
    private static List<string> NamesIn(X509Certificate2 certificate)
    {
        var names = new List<string>();
        try
        {
            foreach (var extension in certificate.Extensions)
            {
                if (extension.Oid?.Value != "2.5.29.17") continue;
                var san = new X509SubjectAlternativeNameExtension(extension.RawData);
                names.AddRange(san.EnumerateDnsNames().Select(Display));
                names.AddRange(san.EnumerateIPAddresses().Select(ip => ip.ToString()));
            }
        }
        catch (CryptographicException)
        {
            // A name list that cannot be read is described by the subject instead; the certificate was refused for the name either way.
        }
        return names;
    }

    /// <summary>
    /// Text from a certificate, made safe to show as the product's own sentence: no control or formatting characters (line breaks, bidirectional
    /// overrides), no double quote to close the quotation it is shown in, and at most <see cref="DisplayLength"/> characters.
    /// </summary>
    internal static string Display(string? text)
    {
        var shown = new StringBuilder();
        var count = 0;
        foreach (var rune in (text ?? string.Empty).EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (Rune.IsControl(rune) || category is UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator) continue;
            if (count == DisplayLength) { shown.Append('…'); break; }
            shown.Append(rune.Value == '"' ? new Rune('\'') : rune);
            count++;
        }
        return shown.ToString();
    }
}
