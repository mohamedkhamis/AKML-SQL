using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace AkmlSql.Core.Config
{
    /// <summary>One stored SQL-auth credential. The password is DPAPI-encrypted
    /// (<c>dpapi:&lt;base64&gt;</c>); plaintext is never persisted.</summary>
    public sealed class SqlCredentialEntry
    {
        public string Server { get; set; } = string.Empty;
        public string Login { get; set; } = string.Empty;
        public string EncryptedPassword { get; set; } = string.Empty;
    }

    /// <summary>
    /// Spec 029. Per-user store of SQL Server-authentication passwords keyed by (server, login),
    /// so the out-of-process engine can connect with SQL auth for IntelliSense. Passwords are
    /// encrypted at rest with Windows DPAPI (CurrentUser scope + app entropy) and saved to
    /// <c>%AppData%\AKML SQL\sql-credentials.json</c> via an atomic temp+rename write
    /// (mirrors <see cref="ConfigManager"/>). All public methods are guarded by a process-wide
    /// lock to make read-modify-write atomic.
    /// </summary>
    public static class SqlCredentialStore
    {
        private const string EncryptedPrefix = "dpapi:";
        private static readonly byte[] AppEntropy = ComputeEntropy();
        private static readonly object _gate = new object();

        // netstandard2.0-safe: SHA256.HashData is .NET5+, so hash via an instance.
        private static byte[] ComputeEntropy()
        {
            using var sha = SHA256.Create();
            return sha.ComputeHash(Encoding.UTF8.GetBytes("AkmlSql-SqlCred-v1"));
        }

        // Same serializer options as ConfigManager (the TypeInfoResolver line is required for the
        // .NET 10 trimmed engine where reflection-based serialization is disabled).
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
        };

        // Same directory as config.json (%AppData%\AKML SQL), derived from ConfigFilePath so we
        // don't depend on a separate Constants member name.
        private static string FilePath
        {
            get
            {
                var dir = Path.GetDirectoryName(Constants.ConfigFilePath);
                if (string.IsNullOrEmpty(dir))
                    dir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AKML SQL");
                return Path.Combine(dir, "sql-credentials.json");
            }
        }

        /// <summary>Decrypts the password for (server, login), or returns false if none is stored.
        /// A single entry whose blob fails to decrypt (e.g. roamed profile) is removed and treated as
        /// absent — it never blocks the other entries.</summary>
        public static bool TryGet(string server, string login, out string password)
        {
            password = string.Empty;
            if (string.IsNullOrEmpty(server) || string.IsNullOrEmpty(login)) return false;

            lock (_gate)
            {
                var list = LoadList();
                var entry = list.FirstOrDefault(e =>
                    string.Equals(e.Server, server, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(e.Login, login, StringComparison.OrdinalIgnoreCase));
                if (entry == null) return false;

                try
                {
                    password = Decrypt(entry.EncryptedPassword);
                    return !string.IsNullOrEmpty(password);
                }
                catch (CryptographicException ex)
                {
                    Log.Warning(ex, "SqlCredentialStore: could not decrypt credential for {Server}/{Login}; dropping it", server, login);
                    list.Remove(entry);
                    SaveList(list);
                    password = string.Empty;
                    return false;
                }
            }
        }

        /// <summary>True if a credential is stored for (server, login) (without decrypting).</summary>
        public static bool Has(string server, string login)
        {
            if (string.IsNullOrEmpty(server) || string.IsNullOrEmpty(login)) return false;
            lock (_gate)
            {
                return LoadList().Any(e =>
                    string.Equals(e.Server, server, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(e.Login, login, StringComparison.OrdinalIgnoreCase));
            }
        }

        /// <summary>Returns the (server, login) pairs that have a stored credential, for a management UI.
        /// Passwords are NEVER returned — only the keys.</summary>
        public static List<(string Server, string Login)> List()
        {
            lock (_gate)
            {
                var result = new List<(string, string)>();
                foreach (var e in LoadList())
                    result.Add((e.Server, e.Login));
                return result;
            }
        }

        /// <summary>Encrypts and stores the password for (server, login), replacing any existing entry.</summary>
        public static void Save(string server, string login, string password)
        {
            if (string.IsNullOrEmpty(server) || string.IsNullOrEmpty(login)) return;
            lock (_gate)
            {
                var list = LoadList();
                list.RemoveAll(e =>
                    string.Equals(e.Server, server, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(e.Login, login, StringComparison.OrdinalIgnoreCase));
                list.Add(new SqlCredentialEntry
                {
                    Server = server,
                    Login = login,
                    EncryptedPassword = Encrypt(password)
                });
                SaveList(list);
            }
        }

        /// <summary>Removes the stored credential for (server, login), if any.</summary>
        public static void Remove(string server, string login)
        {
            if (string.IsNullOrEmpty(server) || string.IsNullOrEmpty(login)) return;
            lock (_gate)
            {
                var list = LoadList();
                int removed = list.RemoveAll(e =>
                    string.Equals(e.Server, server, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(e.Login, login, StringComparison.OrdinalIgnoreCase));
                if (removed > 0) SaveList(list);
            }
        }

        // --- internals (callers hold _gate) ---

        private static List<SqlCredentialEntry> LoadList()
        {
            try
            {
                var path = FilePath;
                if (!File.Exists(path)) return new List<SqlCredentialEntry>();
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<List<SqlCredentialEntry>>(json, SerializerOptions)
                       ?? new List<SqlCredentialEntry>();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "SqlCredentialStore: failed to read store; treating as empty");
                return new List<SqlCredentialEntry>();
            }
        }

        private static void SaveList(List<SqlCredentialEntry> list)
        {
            try
            {
                var path = FilePath;
                var dir = Path.GetDirectoryName(path);
                if (dir != null) Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(list, SerializerOptions);
                var tempPath = path + ".tmp";
                File.WriteAllText(tempPath, json);
#if NETSTANDARD2_0
                if (File.Exists(path)) File.Replace(tempPath, path, null);
                else File.Move(tempPath, path);
#else
                File.Move(tempPath, path, overwrite: true);
#endif
            }
            catch (Exception ex)
            {
                Log.Error(ex, "SqlCredentialStore: failed to save store");
            }
        }

        private static string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return string.Empty;
            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            try
            {
                var cipher = ProtectedData.Protect(plainBytes, AppEntropy, DataProtectionScope.CurrentUser);
                return EncryptedPrefix + Convert.ToBase64String(cipher);
            }
            finally
            {
                Array.Clear(plainBytes, 0, plainBytes.Length); // portable across netstandard2.0 + net10
            }
        }

        private static string Decrypt(string encrypted)
        {
            if (string.IsNullOrEmpty(encrypted)) return string.Empty;
            if (!encrypted.StartsWith(EncryptedPrefix, StringComparison.Ordinal)) return string.Empty;
            var cipher = Convert.FromBase64String(encrypted.Substring(EncryptedPrefix.Length));
            var plainBytes = ProtectedData.Unprotect(cipher, AppEntropy, DataProtectionScope.CurrentUser);
            try { return Encoding.UTF8.GetString(plainBytes); }
            finally { Array.Clear(plainBytes, 0, plainBytes.Length); }
        }
    }
}
