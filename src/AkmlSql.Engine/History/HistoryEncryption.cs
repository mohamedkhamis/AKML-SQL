using System.Security.Cryptography;
using Serilog;

namespace AkmlSql.Engine.History;

/// <summary>
/// Provides optional AES-256 encryption at rest for the SQLite history database,
/// using Windows DPAPI (<see cref="ProtectedData"/>) to protect the AES key.
/// <para>
/// Key material is stored in <c>%AppData%\AKML SQL\history\sqlhistory.key</c>,
/// encrypted with <see cref="DataProtectionScope.CurrentUser"/> so that only the
/// current Windows user can decrypt it.
/// </para>
/// <para>
/// Encrypted database files are prefixed with a 16-byte magic header
/// (<c>AKML-SQL-ENC\x01\x00\x00\x00</c>) followed by a 16-byte IV and the
/// AES-256-CBC ciphertext.
/// </para>
/// </summary>
public static class HistoryEncryption
{
    /// <summary>
    /// Magic bytes written at the start of an encrypted database file.
    /// 12 ASCII bytes + 4-byte version (little-endian 1).
    /// </summary>
    private static readonly byte[] MagicHeader =
    [
        (byte)'A', (byte)'K', (byte)'M', (byte)'L',
        (byte)'-', (byte)'S', (byte)'Q', (byte)'L',
        (byte)'-', (byte)'E', (byte)'N', (byte)'C',
        0x01, 0x00, 0x00, 0x00
    ];

    private const int AesKeySizeBytes = 32; // 256 bits

    /// <summary>
    /// Ensures the DPAPI-protected AES key file exists. If it does not, generates
    /// a new 32-byte random AES key, protects it with DPAPI for the current user,
    /// and writes the protected blob to <c>sqlhistory.key</c>.
    /// </summary>
    /// <returns>The absolute path to the key file.</returns>
    public static async Task<string> EnsureKeyAsync()
    {
        var keyPath = GetKeyPath();
        if (File.Exists(keyPath))
        {
            Log.Debug("HistoryEncryption: key file already exists at {Path}", keyPath);
            return keyPath;
        }

        var keyDir = Path.GetDirectoryName(keyPath)!;
        Directory.CreateDirectory(keyDir);

        // Generate a cryptographically random AES-256 key
        var rawKey = RandomNumberGenerator.GetBytes(AesKeySizeBytes);

        try
        {
            // Protect with DPAPI (CurrentUser scope — only this Windows user can decrypt)
            var protectedKey = ProtectedData.Protect(rawKey, null, DataProtectionScope.CurrentUser);

            await File.WriteAllBytesAsync(keyPath, protectedKey);
            Log.Information("HistoryEncryption: generated and saved new AES-256 key at {Path}", keyPath);
        }
        finally
        {
            // Zero out the raw key material from memory
            CryptographicOperations.ZeroMemory(rawKey);
        }

        return keyPath;
    }

    /// <summary>
    /// Encrypts the SQLite database file in-place with AES-256-CBC.
    /// The output file is prefixed with the magic header, followed by a random 16-byte IV,
    /// then the ciphertext.
    /// </summary>
    /// <param name="dbPath">Absolute path to the plaintext <c>.db</c> file.</param>
    public static async Task EncryptDatabaseAsync(string dbPath)
    {
        if (!File.Exists(dbPath))
        {
            Log.Warning("HistoryEncryption: database file not found at {Path}, skipping encryption", dbPath);
            return;
        }

        if (IsEncrypted(dbPath))
        {
            Log.Debug("HistoryEncryption: file is already encrypted, skipping");
            return;
        }

        var aesKey = await LoadKeyAsync();
        var plaintext = await File.ReadAllBytesAsync(dbPath);

        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = aesKey;
        aes.GenerateIV();

        byte[] ciphertext;
        try
        {
            ciphertext = aes.EncryptCbc(plaintext, aes.IV, PaddingMode.PKCS7);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(aesKey);
        }

        // Write: magic + IV + ciphertext (atomic via temp file)
        var tempPath = dbPath + ".enc.tmp";
        try
        {
            await using var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await fs.WriteAsync(MagicHeader);
            await fs.WriteAsync(aes.IV);
            await fs.WriteAsync(ciphertext);
            await fs.FlushAsync();
        }
        catch
        {
            try { File.Delete(tempPath); } catch { /* best-effort cleanup */ }
            throw;
        }

        File.Move(tempPath, dbPath, overwrite: true);
        Log.Information("HistoryEncryption: database encrypted successfully ({Bytes} bytes plaintext)", plaintext.Length);
    }

    /// <summary>
    /// Decrypts an AES-256-CBC encrypted database file back to plaintext SQLite format.
    /// If the file is not encrypted (no magic header), this method is a no-op.
    /// </summary>
    /// <param name="dbPath">Absolute path to the encrypted <c>.db</c> file.</param>
    public static async Task DecryptDatabaseAsync(string dbPath)
    {
        if (!File.Exists(dbPath))
        {
            Log.Warning("HistoryEncryption: database file not found at {Path}, skipping decryption", dbPath);
            return;
        }

        if (!IsEncrypted(dbPath))
        {
            Log.Debug("HistoryEncryption: file is not encrypted, skipping decryption");
            return;
        }

        var allBytes = await File.ReadAllBytesAsync(dbPath);

        // Layout: [16 magic][16 IV][ciphertext...]
        const int headerSize = 16;
        const int ivSize = 16;

        if (allBytes.Length < headerSize + ivSize + 1)
        {
            Log.Warning("HistoryEncryption: encrypted file too small ({Bytes} bytes), cannot decrypt", allBytes.Length);
            return;
        }

        var iv = new byte[ivSize];
        Array.Copy(allBytes, headerSize, iv, 0, ivSize);

        var ciphertext = new byte[allBytes.Length - headerSize - ivSize];
        Array.Copy(allBytes, headerSize + ivSize, ciphertext, 0, ciphertext.Length);

        var aesKey = await LoadKeyAsync();

        byte[] plaintext;
        try
        {
            using var aes = Aes.Create();
            aes.KeySize = 256;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = aesKey;

            plaintext = aes.DecryptCbc(ciphertext, iv, PaddingMode.PKCS7);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(aesKey);
        }

        // Atomic write via temp file
        var tempPath = dbPath + ".dec.tmp";
        try
        {
            await File.WriteAllBytesAsync(tempPath, plaintext);
        }
        catch
        {
            try { File.Delete(tempPath); } catch { /* best-effort cleanup */ }
            throw;
        }

        File.Move(tempPath, dbPath, overwrite: true);
        Log.Information("HistoryEncryption: database decrypted successfully ({Bytes} bytes)", plaintext.Length);
    }

    /// <summary>
    /// Checks whether the specified file begins with the AKML-SQL encryption magic header.
    /// Returns <c>false</c> if the file does not exist or is too small to contain the header.
    /// </summary>
    /// <param name="dbPath">Absolute path to the database file.</param>
    public static bool IsEncrypted(string dbPath)
    {
        try
        {
            if (!File.Exists(dbPath))
                return false;

            using var fs = new FileStream(dbPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (fs.Length < MagicHeader.Length)
                return false;

            Span<byte> header = stackalloc byte[MagicHeader.Length];
            var bytesRead = fs.Read(header);
            if (bytesRead < MagicHeader.Length)
                return false;

            return header.SequenceEqual(MagicHeader);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "HistoryEncryption: could not check encryption status of {Path}", dbPath);
            return false;
        }
    }

    /// <summary>
    /// Loads the AES key from the DPAPI-protected key file and returns the raw key bytes.
    /// The caller is responsible for zeroing the returned array when done.
    /// </summary>
    private static async Task<byte[]> LoadKeyAsync()
    {
        var keyPath = GetKeyPath();
        if (!File.Exists(keyPath))
        {
            throw new InvalidOperationException(
                $"HistoryEncryption: key file not found at '{keyPath}'. Call EnsureKeyAsync() first.");
        }

        var protectedKey = await File.ReadAllBytesAsync(keyPath);
        var rawKey = ProtectedData.Unprotect(protectedKey, null, DataProtectionScope.CurrentUser);

        if (rawKey.Length != AesKeySizeBytes)
        {
            throw new CryptographicException(
                $"HistoryEncryption: expected {AesKeySizeBytes}-byte key, got {rawKey.Length} bytes.");
        }

        return rawKey;
    }

    /// <summary>
    /// Returns the canonical path to the DPAPI-protected key file.
    /// </summary>
    private static string GetKeyPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AKML SQL", "history", "sqlhistory.key");
    }
}
