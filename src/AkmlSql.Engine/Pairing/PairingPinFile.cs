using System;
using System.IO;
using System.Text;
using Serilog;

namespace AkmlSql.Engine.Pairing
{
    /// <summary>
    /// Spec 026 (M4 closure) FR-008 / FR-009 / FR-013. Persists the current pairing PIN to
    /// <c>%CommonAppData%/AKML SQL Web/pairing-pin.txt</c> so the installer's
    /// <c>Web_PostInstall</c> can read it into <c>INSTALL-SUMMARY.txt</c>.
    ///
    /// <para><see cref="PairingService"/> deliberately stays free of file I/O; <c>EngineHost</c>
    /// wires this writer to <see cref="PairingService.PinChanged"/> plus a one-shot
    /// <see cref="Publish"/> of <see cref="PairingService.CurrentPin"/> immediately after
    /// subscription (the initial PIN is minted inside the <see cref="PairingService"/> constructor,
    /// before any external subscriber can attach).</para>
    ///
    /// <para>The write is atomic (temp + rename), UTF-8 with no BOM and no trailing newline.
    /// Write failures (disk full, ACL denied) are caught and logged — they MUST NOT crash engine
    /// startup (FR-013); the PIN is still served from memory via the in-process API.</para>
    /// </summary>
    public sealed class PairingPinFile
    {
        private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
        private readonly string _path;

        public PairingPinFile(string path)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
        }

        /// <summary>
        /// Atomically write the PIN. No-ops on an empty PIN so a consumed PIN (when
        /// <see cref="PairingService.CurrentPin"/> reports empty) leaves the last minted value on
        /// disk — the file always reflects the most recently *minted* PIN (data-model E2). Never throws.
        /// </summary>
        public void Publish(string pin)
        {
            if (string.IsNullOrEmpty(pin)) return;
            try
            {
                // Spec 026 (M4 closure) M1: create the shared-state dir hardened (Admins+SYSTEM only)
                // if we are its creator, so the PIN is never written under a world-readable ACL.
                var dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir)) SecureDirectory.EnsureSecured(dir);

                var tmp = _path + ".tmp";
                File.WriteAllText(tmp, pin, Utf8NoBom);
                File.Move(tmp, _path, overwrite: true);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "PairingPinFile: failed to write {Path}", _path);
            }
        }
    }
}
