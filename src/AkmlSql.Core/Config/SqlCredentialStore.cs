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
    ///
    /// <para>
    /// Spec 043 follow-up (review, round 3): a store that cannot be read (the file is locked, access
    /// is denied, the disk fails) is never written. The entries are still in the file, only unreadable
    /// for now: writing a list built from nothing dropped every other credential (the SSMS extension's
    /// too). Lookups answer "not found", and <see cref="Save(string, string, string, string[])"/> and
    /// <see cref="Remove"/> write nothing and return false. A file that is not valid JSON still reads as
    /// empty, and the next save replaces it.
    /// </para>
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

        /// <summary>Decrypts the password for (server, login), or returns false if none is stored
        /// (or the store cannot be read). A single entry whose blob fails to decrypt (e.g. roamed
        /// profile) is removed and treated as absent — it never blocks the other entries.</summary>
        public static bool TryGet(string server, string login, out string password)
        {
            password = string.Empty;
            if (string.IsNullOrEmpty(server) || string.IsNullOrEmpty(login)) return false;

            lock (_gate)
            {
                if (!TryLoadList(out var list)) return false;
                var entry = list.FirstOrDefault(e =>
                    string.Equals(e.Server, server, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(e.Login, login, StringComparison.OrdinalIgnoreCase));
                if (entry == null) return false;

                try
                {
                    password = Decrypt(entry.EncryptedPassword);
                    return !string.IsNullOrEmpty(password);
                }
                catch (Exception ex) when (ex is CryptographicException || ex is FormatException)
                {
                    Log.Warning(ex, "SqlCredentialStore: could not decrypt credential for {Server}/{Login}; dropping it", server, login);
                    list.Remove(entry);
                    SaveList(list);
                    password = string.Empty;
                    return false;
                }
            }
        }

        /// <summary>True if a credential is stored for (server, login) (without decrypting). False when
        /// the store cannot be read.</summary>
        public static bool Has(string server, string login)
        {
            if (string.IsNullOrEmpty(server) || string.IsNullOrEmpty(login)) return false;
            lock (_gate)
            {
                return TryLoadList(out var list) && list.Any(e =>
                    string.Equals(e.Server, server, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(e.Login, login, StringComparison.OrdinalIgnoreCase));
            }
        }

        /// <summary>Returns the (server, login) pairs that have a stored credential, for a management UI
        /// (none when the store cannot be read). Passwords are NEVER returned — only the keys.</summary>
        public static List<(string Server, string Login)> List()
        {
            lock (_gate)
            {
                var result = new List<(string, string)>();
                TryLoadList(out var list);
                foreach (var e in list)
                    result.Add((e.Server, e.Login));
                return result;
            }
        }

        /// <summary>Encrypts and stores the password for (server, login), replacing any existing entry. True when the store
        /// file was written; false when it could not be read or written (logged) — the caller must not report the password as
        /// kept, and an unreadable store is left as it is. Encryption failures (DPAPI) are thrown, as before.</summary>
        public static bool Save(string server, string login, string password) =>
            Save(server, login, password, Array.Empty<string>());

        /// <summary>
        /// Spec 043 follow-up (review): as <see cref="Save(string, string, string)"/>, and removes the entries of the same login
        /// under <paramref name="alsoRemoveServers"/> in the same write, under one lock — so two saves for the same login under
        /// different keys, each replacing the other, cannot both run their removal after the other's save and leave nothing.
        /// True when the store file was written (the new entry is kept and the others are gone); false when it could not be
        /// read or written (logged): the store is then as it was.
        /// </summary>
        public static bool Save(string server, string login, string password, params string[] alsoRemoveServers)
        {
            if (string.IsNullOrEmpty(server) || string.IsNullOrEmpty(login)) return false;
            var replaced = alsoRemoveServers ?? Array.Empty<string>();
            lock (_gate)
            {
                // Spec 043 follow-up (review, round 3): a list that could not be read is not the store's content; writing it
                // with only this entry would drop every other credential.
                if (!TryLoadList(out var list)) return false;
                list.RemoveAll(e =>
                    string.Equals(e.Login, login, StringComparison.OrdinalIgnoreCase) &&
                    (string.Equals(e.Server, server, StringComparison.OrdinalIgnoreCase) ||
                     replaced.Any(r => !string.IsNullOrEmpty(r) && string.Equals(e.Server, r, StringComparison.OrdinalIgnoreCase))));
                list.Add(new SqlCredentialEntry
                {
                    Server = server,
                    Login = login,
                    EncryptedPassword = Encrypt(password)
                });
                return SaveList(list);
            }
        }

        /// <summary>Removes the stored credential for (server, login), if any. True when there was nothing to remove or the
        /// removal was written; false when the store file could not be read or written (logged) — the entry may then still be
        /// there, and the caller must not report it as forgotten. (Spec 043 follow-up, review; the SSMS callers ignore the
        /// result.)</summary>
        public static bool Remove(string server, string login)
        {
            if (string.IsNullOrEmpty(server) || string.IsNullOrEmpty(login)) return true;
            lock (_gate)
            {
                // Spec 043 follow-up (review, round 3): an unreadable store is not "nothing to remove".
                if (!TryLoadList(out var list)) return false;
                int removed = list.RemoveAll(e =>
                    string.Equals(e.Server, server, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(e.Login, login, StringComparison.OrdinalIgnoreCase));
                return removed == 0 || SaveList(list);
            }
        }

        // --- internals (callers hold _gate) ---

        /// <summary>
        /// Spec 043 follow-up (review, round 3): reads the store into <paramref name="list"/>. A missing file is an empty store.
        /// A file that is not valid JSON reads as empty too (logged): nothing in it can be read again, and the next write
        /// replaces it, which heals it. Any other failure to read it (a sharing violation, a denied access, a disk error) returns
        /// false with an empty list: the entries are still in the file, so the caller answers "not found" and never writes.
        /// </summary>
        private static bool TryLoadList(out List<SqlCredentialEntry> list)
        {
            list = new List<SqlCredentialEntry>();
            string json;
            try
            {
                var path = FilePath;
                if (!File.Exists(path)) return true;
                json = File.ReadAllText(path);
            }
            catch (Exception ex) when (ex is FileNotFoundException || ex is DirectoryNotFoundException)
            {
                return true;   // removed between the check and the read: an empty store
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "SqlCredentialStore: could not read the store; nothing is written until it can be read");
                return false;
            }

            try
            {
                list = JsonSerializer.Deserialize<List<SqlCredentialEntry>>(json, SerializerOptions)
                       ?? new List<SqlCredentialEntry>();
                list.RemoveAll(e => e == null);   // "[null]" is valid JSON, not an entry
                return true;
            }
            catch (JsonException ex)
            {
                Log.Warning(ex, "SqlCredentialStore: the store is not valid JSON; treating it as empty (the next save replaces it)");
                list = new List<SqlCredentialEntry>();
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "SqlCredentialStore: could not read the store; nothing is written until it can be read");
                list = new List<SqlCredentialEntry>();
                return false;
            }
        }

        private static bool SaveList(List<SqlCredentialEntry> list)
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
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "SqlCredentialStore: failed to save store");
                return false;
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
