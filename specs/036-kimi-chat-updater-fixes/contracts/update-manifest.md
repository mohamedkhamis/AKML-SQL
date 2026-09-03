# Contract: update manifest and the updater CLI

Satisfies **FR-033 – FR-046**.

**Anchors**: `src/AkmlSql.Core/Constants.cs:24` · `src/AkmlSql.Core/Update/UpdateManifest.cs` · `src/AkmlSql.Updater/Program.cs` · `src/AkmlSql.Shell.Shared/Update/UpdateLauncher.cs` · `src/AkmlSql.Shell.Shared/Commands/CheckUpdateCommand.cs:56-70` · `scripts/deploy-site-iis.ps1:100-172` · `src/AkmlSql.Site/wwwroot/releases.json`

---

## 1. The endpoint

| | Value |
|---|---|
| Today | `https://updates.akmlsql.com/manifest.json` — **does not resolve; no build has ever checked successfully** |
| Required | `https://akml.khamis.work/update-manifest.json` |

The host is the existing product site (`Site:BaseUrl`, `src/AkmlSql.Site/appsettings.json:10`), which already serves `releases.json` from `wwwroot`. Adding one more static file introduces no infrastructure — FR-033 and the spec's "no new servers" boundary are both satisfied.

`tests/AkmlSql.Core.Tests/ConstantsTests.cs:45` asserts the dead string today and must be updated with the constant.

## 2. Manifest document

Served at `/update-manifest.json`, generated into `src/AkmlSql.Site/wwwroot/` by the deploy script. Matches `UpdateManifest` exactly (camelCase, as `Program.cs` configures):

```json
{
  "version": "1.26.0903.0900",
  "downloadUrl": "https://github.com/mohamedkhamis/AKML-SQL/releases/download/v1.26.0903.0900/AKMLSQLSetup-1.26.0903.0900.exe",
  "releaseNotesUrl": "https://github.com/mohamedkhamis/AKML-SQL/releases",
  "minimumOsVersion": "10.0",
  "sha256Hash": "9ad4cd1774679948ec0a34936bc2b6f7b922bcbd6e613903e77ed411e5a6eae7"
}
```

| Field | Source in the deploy script |
|---|---|
| `version` | `(Get-Item $ReleaseExe).VersionInfo.FileVersion.Trim()` |
| `downloadUrl` | `$cdnUrl` when the `gh` upload succeeded, else the absolute site `/dl/...` URL |
| `releaseNotesUrl` | the entry's `releaseNotesUrl` |
| `minimumOsVersion` | the entry's `minimumOsVersion` |
| `sha256Hash` | `(Get-FileHash $ReleaseExe -Algorithm SHA256).Hash.ToLower()` |

**Generation rule (FR-036)**: emit the manifest from the **same `$entry` object** already constructed for `releases.json` at `scripts/deploy-site-iis.ps1:158-172` — one write, two files, no second computation of version or hash. Hand-editing either file is prohibited.

**Ordering trap**: the site serves `wwwroot` through `app.MapStaticAssets()` (`src/AkmlSql.Site/Program.cs:280`), which resolves its asset list from a **build-time** manifest. A file dropped into `wwwroot` *after* `dotnet publish` is not served. The deploy script already stages the release before its publish step (`scripts/deploy-site-iis.ps1`, staging block precedes `# --- 1. Publish ---`), so writing the manifest in the same block is correct — but it must stay in that block. Writing it later would silently 404, and the check would fail exactly as it does today.

**Absolute URL requirement**: `releases.json` stores `downloadUrl` as a site-relative path (`downloads/AKMLSQLSetup-*.exe`), which is fine for the download page but useless to the updater. The manifest's `downloadUrl` is always absolute — prefer `cdnUrl`; when the GitHub upload was skipped or failed, fall back to `$BaseUrl + '/dl/' + $fileName`.

**Consistency invariant (FR-036)**: the newest entry in `releases.json` and the manifest name the same version, the same file and the same hash. Asserted by a test in `tests/AkmlSql.Site.Tests/`, not left to discipline.

## 3. Updater CLI

### `--check` (existing, endpoint changes)

Unchanged behaviour: fetch the manifest, compare with `IsNewerVersion` (strips SemVer pre-release before `new Version(...)`, `Program.cs:121-131`), write `update-available.json` atomically, delete a stale result when up to date, stamp `LastUpdateCheck`. Always exits 0 — a failed check is not a user-facing error (FR-041).

### `--download` (new)

```
AkmlSql.Updater.exe --download
```

```
1. read %AppData%\AKML SQL\cache\update-available.json
   if not Available            -> exit 0, nothing to do
   if already DownloadState=verified and the file still exists and still hashes -> exit 0
2. DownloadState := "downloading"; persist
3. GET DownloadUrl  -> %AppData%\AKML SQL\cache\AKMLSQLSetup-<version>.exe.partial
4. compute SHA-256 of the completed file
5. if mismatch:  delete the file; DownloadState := "failed";
                 FailureReason := "checksum mismatch"; persist; exit 2
6. rename .partial -> final name
7. VerifiedInstallerPath := absolute final path
   DownloadState        := "verified"; persist; exit 0
```

Rules:

- **Every write to `update-available.json` stays atomic** (temp + `File.Move(overwrite: true)`), as `--check` already does at `Program.cs:75-78`.
- **Cancellation / interruption** leaves no `.partial` behind: delete it in a `finally` unless the run reached step 6 (FR-039a).
- **Exit codes**: `0` success or nothing to do, `2` verification failed, `1` usage error. The shell distinguishes them.
- **HTTPS only.** Reject a non-HTTPS `downloadUrl` before the request, mirroring `CheckUpdateCommand.IsValidHttpsUrl`.
- **Anonymous** (FR-034): no token, no credential, no `gh` CLI. GitHub release assets are public.

## 4. Shell flow

```
startup            -> UpdateLauncher.LaunchIfDue()      // 24h throttle, unchanged
manual menu item   -> UpdateLauncher.LaunchUpdater()    // bypasses the throttle (FR-042)
                                                        // already public and already separate
notification       <- UpdateNotifier.CheckForPendingUpdate() reads the result file
user: "Update now" -> launch updater with --download, show progress, allow cancel
verified           -> confirmation dialog naming the hosts that must close
user confirms      -> Process.Start(VerifiedInstallerPath)   // canonicalised, no /VERYSILENT
user declines      -> nothing installed, nothing closed, offer retained
```

**Confirmation dialog** must follow the FR-005 safety convention from `CLAUDE.md`: Cancel is `IsCancel = true` and holds initial focus on `Loaded`; the proceed button is **not** the default. Reference: `src/AkmlSql.Shell.Shared/Safety/SafetyWarningDialog.cs`.

**Not silent**: the installer runs with its normal UI. `/VERYSILENT` stays reserved for the documented unattended-deployment path in `doc/deployment.md`.

**Manual check outcomes** (FR-042): report all three — up to date, update available, check failed. The automatic path reports none of them (FR-041).

## 5. Installer

No changes. `AppId` is fixed (`AkmlSqlSetup.iss:74-79`) so upgrades are in place; `CloseApplications=yes` with `CloseApplicationsFilter=Ssms.exe,devenv.exe` (`:126-127`) handles running hosts; `VersionInfoVersion` (`:94`) is what the deploy script reads back, closing the version loop. User data under `%AppData%\AKML SQL\` is not installer-managed, which is why it survives — FR-043 requires demonstrating that, not building it.

## 6. Test coverage

| Test | Location | Asserts |
|---|---|---|
| `UpdateManifestUrl` points at the live host | `tests/AkmlSql.Core.Tests/ConstantsTests.cs` | HTTPS, resolvable host, not `updates.akmlsql.com` |
| Manifest ≡ newest release entry | `tests/AkmlSql.Site.Tests/` | version, file and hash agree (FR-036) |
| Manifest `downloadUrl` is absolute HTTPS | `tests/AkmlSql.Site.Tests/` | never a relative path |
| Version comparison | `tests/AkmlSql.Core.Tests/` | strictly-newer only; equal and older → no update (FR-037) |
| Checksum mismatch aborts | `tests/AkmlSql.Installer.Tests/` | file deleted, exit 2, `FailureReason` set (FR-040) |
| Cancel leaves no partial | `tests/AkmlSql.Installer.Tests/` | no `.partial` on disk (FR-039a) |
| Result file writes are atomic | `tests/AkmlSql.Core.Tests/` | temp + move, no partial JSON |
| Manual check bypasses the throttle | `tests/AkmlSql.Shell.Shared.Tests/` | `LaunchUpdater` called with a recent `LastUpdateCheck` (FR-042) |
| **Clean-machine end-to-end** | **manual — `doc/progress.md`** | **FR-046: publish → detect → notify → download → verify → install → restart → version agrees** |

The last row cannot be automated. It requires a clean Windows machine with the previous published build installed and a real newer release. Evidence — date, steps, results — is recorded in `doc/progress.md` and referenced from `tasks.md`.
