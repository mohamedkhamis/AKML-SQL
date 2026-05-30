# Contract: Installer Smoke Suite (US5)

Defines the opt-in, host-gated test suite that asserts a real install. Covers FR-027..FR-032.

## C1 — Project

`tests/AkmlSql.Installer.Tests/AkmlSql.Installer.Tests.csproj`:

- TFM `net10.0`; references `xunit`, `Microsoft.NET.Test.Sdk`, **`Xunit.SkippableFact`**.
- Registered in `AKML-SQL.slnx`.
- Not referenced by any production project (test-only).

## C2 — Opt-in trait + host gate

- Every test class carries `[Trait("Category","InstallerSmoke")]`. The default `dotnet test` run excludes it; opt in with `dotnet test --filter Category=InstallerSmoke`. Mirrors spec 024 `ParityBaseline` + spec 025 `BridgeE2E`.
- Every test is a `[SkippableFact]` (or `[SkippableTheory]`) that calls `Skip.IfNot(IsAdministrator() && IsIisInstalled(), "Requires admin + IIS")` first — so on a CI / non-admin / non-IIS host the suite reports **Skipped**, never Failed.
- `IsAdministrator()` via `WindowsIdentity.GetCurrent()` + `WindowsPrincipal.IsInRole(WindowsBuiltInRole.Administrator)`; `IsIisInstalled()` via the same registry+file check as the installer.

## C3 — Fixture lifecycle (`InstallerSmokeFixture : IAsyncLifetime`)

1. Locate the prebuilt `src/AkmlSql.Installer/Output/AKMLSQLSetup.exe`; if absent, fail every test with "Build the installer first via ISCC.exe AkmlSqlSetup.iss" (FR-032).
2. Capture the pre-install hash of `%AppData%\AKML SQL\config.json` (if present) for the SC-007 check.
3. Run a silent install: `AKMLSQLSetup.exe /VERYSILENT /ACCEPTEULA /WEB_HOST=IIS /WEB_EXPOSURE=LAN /WEB_PORT=<free> /BRIDGE_PORT=<free> /LOG=<temp>`.
4. Read `%CommonAppData%\AKML SQL Web\INSTALL-SUMMARY.txt` for the thumbprint + URL the assertions need.
5. On dispose: silent uninstall; capture the post-uninstall hash of the plugin config.

The fixture picks two distinct free ports so the suite never collides with a real install on the dev host.

## C4 — Assertions (FR-030, one test each)

| Test class | Assertions |
|------------|-----------|
| `IisProvisioningTests` | (a) `Get-Website -Name AkmlSqlWeb` bound on the chosen IIS port; (b) the five MIME types registered; (c) `Content-Security-Policy` header present on a `HEAD` to `http://localhost:<IisPort>/` |
| `LanTlsTests` | (d) `netsh http show sslcert ipport=0.0.0.0:<BridgePort>` thumbprint == summary thumbprint; (e) `Get-NetFirewallRule -DisplayName "AKML SQL Web Engine"` exists |
| `ReRunAndUninstallTests` | (f) `INSTALL-SUMMARY.txt` non-empty + has a `URL:` line; (g) plugin-config hash identical pre-install / post-install / post-uninstall (SC-007); plus an install→uninstall→re-install cycle (FR-031) |

PowerShell-backed assertions run via `System.Management.Automation` or `Process.Start("powershell.exe", ...)` and parse stdout.

## C5 — Failure messages

Each assertion failure names the missing artefact, e.g. `Assert.True(mimeFound, "Expected .wasm MIME type on AkmlSqlWeb; got 0 matches")`, so a regression is diagnosable from the test output alone (FR-030 last clause).

**Verification**: on an IIS+admin host, `dotnet test tests/AkmlSql.Installer.Tests --filter Category=InstallerSmoke` runs the full cycle in < 5 min and is green (SC-006); on a non-admin host every test is Skipped; the default `dotnet test` (no filter) does not run any of them.
