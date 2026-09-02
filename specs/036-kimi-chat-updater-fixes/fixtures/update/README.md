# Update-channel fixture (T003)

Staged manifest for the spec-036 US5 local scenarios (quickstart 41-51). Not shipped.

- `update-manifest.json` names version **1.999.0902.0000**, which is strictly newer than any
  dev build (`1.{commitCount}.{MMddHHmm}`, ~1.526.x at the time of writing), so `IsNewerVersion`
  always fires against a dev updater.
- `downloadUrl` points at the **real** GitHub CDN asset of the latest published release, and
  `sha256Hash` is that asset's real hash (copied from `src/AkmlSql.Site/wwwroot/releases.json`).
  This makes the happy-path `--download` fetch + SHA-256 verify work end to end, anonymously,
  over public HTTPS.

## How to use (quickstart scenarios 41-51)

1. Serve this directory over plain HTTP, e.g.
   `powershell -NoProfile -ExecutionPolicy Bypass -File specs/036-kimi-chat-updater-fixes/fixtures/update/serve.ps1 -Port 8099`
   (or `python -m http.server 8099 --directory specs/036-kimi-chat-updater-fixes/fixtures/update`
   where Python exists). Plain HTTP is fine for the manifest fetch; the HTTPS-only rule applies
   to `downloadUrl`, which is the GitHub CDN.
2. Temporarily point `Constants.UpdateManifestUrl` at
   `http://localhost:8099/update-manifest.json`, rebuild `AkmlSql.Updater`, run the scenarios.
3. Scenario 48 (hash mismatch): edit the fixture's `sha256Hash` (flip one hex digit), re-run
   `--download`, confirm exit 2 + deleted file, then restore the file.
4. Revert `Constants.UpdateManifestUrl` afterwards.
