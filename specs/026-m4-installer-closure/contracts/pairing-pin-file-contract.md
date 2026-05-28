# Contract: Pairing PIN File (US2)

Defines `pairing-pin.txt` — the bridge between the engine's in-memory PIN and the installer's summary. Covers FR-008..FR-013.

## C1 — File

| Property | Value |
|----------|-------|
| Path | `%CommonAppData%\AKML SQL Web\pairing-pin.txt` (sits next to `tokens.json`) |
| Content | exactly the 6-digit decimal PIN; UTF-8; **no** trailing newline; **no** BOM |
| Encoding write | `new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)` |

## C2 — Writer (`PairingPinFile`, NEW)

A small class in `src/AkmlSql.Engine/Pairing/PairingPinFile.cs`:

```csharp
public sealed class PairingPinFile
{
    private readonly string _path;
    public PairingPinFile(string path) { _path = path; }

    public void Publish(string pin)        // atomic temp + rename
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, pin, new UTF8Encoding(false));
            File.Move(tmp, _path, overwrite: true);   // .NET 10 atomic replace
        }
        catch (Exception ex)
        {
            Log.Error(ex, "PairingPinFile: failed to write {Path}", _path);  // swallow — FR-013
        }
    }
}
```

## C3 — Wiring in `EngineHost` (LAN mode only)

```csharp
var pinFile = new PairingPinFile(Path.Combine(commonAppData, "AKML SQL Web", "pairing-pin.txt"));
pairing.PinChanged += (_, pin) => pinFile.Publish(pin);
pinFile.Publish(pairing.CurrentPin);   // capture the initial mint (fired inside the ctor, before subscribe)
```

The one-shot `Publish(CurrentPin)` is mandatory: `PairingService` mints the first PIN inside its constructor (`PairingService.cs:56`), firing `PinChanged` before any subscriber attaches. Without the one-shot call the first PIN is never written.

## C4 — ACL (installer responsibility, not engine)

The installer creates `%CommonAppData%\AKML SQL Web\` (if absent) with an ACL granting **Administrators + SYSTEM** read+write only, before starting the `AkmlSqlWebEngine` service. The LocalSystem engine then writes the file into the already-locked-down directory. Standard-user read is denied (leaked PIN = local operator impersonation). On re-run, an existing directory's ACL is left intact.

## C5 — Reader (`Web_PostInstall`)

After `sc.exe start AkmlSqlWebEngine`:

1. Poll `pairing-pin.txt` for appearance, 30 s timeout (the engine writes it on first start).
2. If present: read, trim, bake `Pairing PIN: <value>` into `INSTALL-SUMMARY.txt` + the success page (LAN mode only).
3. If absent after timeout: write the fallback "Pairing PIN not yet generated — start the AkmlSqlWebEngine service, then re-read %CommonAppData%\AKML SQL Web\INSTALL-SUMMARY.txt"; do **not** fail the install.

Localhost-mode installs omit the PIN line entirely (loopback auto-accepts; no PIN needed).

**Verification**: after a LAN install the file exists with a 6-digit line; `icacls pairing-pin.txt` shows only Administrators + SYSTEM; the install summary shows the PIN; killing+deleting the file and restarting the service regenerates it.
