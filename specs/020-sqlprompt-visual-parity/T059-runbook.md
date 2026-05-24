# T059 — Format Styles editor VSCT wire (runbook)

**Status**: Ready for a focused session. `FormatStylesEditorWindow.Launch()` exists; no menu entry in any host points to it.
**Spec**: 020 / US3 / T059
**Originating PR**: the spec-020 close-out PR that landed Phase C (T031) + Phase D (T044–T048).
**Estimated effort**: 2–3 hours of mechanical work + per-host MSBuild verification.

---

## Goal

Add a "Format Styles…" menu entry in every host (SSMS 20/21/22, VS 2019/22/26) that calls `FormatStylesEditorWindow.Launch()`. The window itself is already built — only the menu wire is missing.

## Why this was deferred from the originating PR

Per the deferral note in `tasks.md`: "Adding a new VSCT command across 6 hosts (one CTO file per shell) is the riskiest mechanical part of US3 and is better done in a focused session." MSBuild-only builds per shell project (no `dotnet build` per CLAUDE.md — VSCT cross-contamination risk). Touching 6 host projects + 1 shared file warrants its own branch + PR so the diff is reviewable in isolation.

---

## File-by-file plan

### 1. Add the command id (1 file)

`src/AkmlSql.Shell.Shared/PackageGuids.cs` — under the `CommandIds` class. The reservation comment near line 94 says `0x0916..0x093F` is reserved for spec 019; only `0x0900..0x0915` are actually used. **Pick `0x0916`** and tighten the reservation comment to `0x0917..0x093F`, OR open a fresh range starting at `0x1000` to keep spec-019's reservation intact. Either is defensible; the existing usage pattern suggests just taking the next free id.

```csharp
// Spec 020 US3 T059 — Format Styles editor launcher
public const int CmdFormatStyles = 0x0916;
```

### 2. Add the command class (1 file)

`src/AkmlSql.Shell.Shared/Commands/FormatStylesCommand.cs` — copy `OptionsCommand.cs` as the structural template (same file lives in the same directory). The body's `Execute` calls `FormatStylesEditorWindow.Launch()`:

```csharp
using System;
using System.ComponentModel.Design;
using System.Windows;
using AkmlSql.Shell.Shared.Formatting;
using Microsoft.VisualStudio.Shell;
using Serilog;
using Constants = AkmlSql.Core.Constants;

namespace AkmlSql.Shell.Shared.Commands
{
    internal sealed class FormatStylesCommand
    {
        private FormatStylesCommand(Package package, OleMenuCommandService commandService)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));

            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdFormatStyles);
            var menuItem = new MenuCommand(Execute, cmdId);
            commandService.AddCommand(menuItem);
        }

        public static FormatStylesCommand Instance { get; private set; }

        public static void Initialize(Package package, OleMenuCommandService commandService)
            => Instance = new FormatStylesCommand(package, commandService);

        private void Execute(object sender, EventArgs e)
        {
            try
            {
                FormatStylesEditorWindow.Launch();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to open Format Styles editor");
                MessageBox.Show(
                    "Failed to open Format Styles editor: " + ex.Message,
                    Constants.ProductName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
```

### 3. Update each host's `.vsct` (6 files)

Files: `src/AkmlSql.Ssms20/AkmlSqlSsms20.vsct`, `src/AkmlSql.Ssms21/AkmlSqlSsms21.vsct`, `src/AkmlSql.Ssms22/AkmlSqlSsms22.vsct`, `src/AkmlSql.VS2019/AkmlSqlVS2019.vsct`, `src/AkmlSql.VS2022/AkmlSqlVS2022.vsct`, `src/AkmlSql.VS2026/AkmlSqlVS2026.vsct`.

In each file:

1. Under `<Commands>/<Buttons>`, add a `<Button>` element with:
   - `guid="guidAkmlSqlCmdSet"`, `id="cmdFormatStyles"` (declare these symbols at the bottom of the file under `<Symbols>` if not present).
   - Parent: same as `cmdOptions` or `cmdEditProfile` uses (look at the existing button in the same file).
   - Strings: `<ButtonText>Format Styles...</ButtonText>` and a tooltip.
2. Under `<CommandPlacements>`, add a `<CommandPlacement>` placing the button into the right host menu:
   - **SSMS 20/21/22**: `guidSHLMainMenu:IDG_VS_TOOLS_EXT_TOOLS` — the SSMS-visible Tools menu. (Per CLAUDE.md: `IDG_VS_MM_TOOLSADDINS` is invisible in SSMS — don't use it for SSMS.)
   - **VS 2019/22/26**: `CommandIds.AkmlSqlMenuGroup` (0x1020) for the AKML submenu, or `IDG_VS_MM_TOOLSADDINS` for top-level Tools. Look at where `cmdOptions` is placed in each VS host's VSCT and match the convention.

The simplest reference: `cmdOptions` is already wired through similarly. Diff against its `<Button>` + `<CommandPlacement>` and mirror.

### 4. Register in each host's package class (6 files)

Find each host's `AkmlSqlPackage.cs` (probably `src/AkmlSql.<Host>/AkmlSql<Host>Package.cs` or similar — `Glob` for `**/AkmlSql*Package.cs`). In its `InitializeAsync` (or wherever the existing `OptionsCommand.Initialize(this, commandService)` call is), add:

```csharp
FormatStylesCommand.Initialize(this, commandService);
```

Mirror the order around the `OptionsCommand` call.

---

## Verification

Per CLAUDE.md — shell projects MUST use full MSBuild (NOT `dotnet build`), and MUST be built per-project (NOT via solution — VSCT cross-contamination):

```bash
MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Enterprise/MSBuild/Current/Bin/MSBuild.exe"

for proj in Ssms20 Ssms21 Ssms22 VS2019 VS2022 VS2026; do
    "$MSBUILD" "src/AkmlSql.${proj}/AkmlSql.${proj}.csproj" -t:Restore -p:Configuration=Release -v:quiet
    "$MSBUILD" "src/AkmlSql.${proj}/AkmlSql.${proj}.csproj" -t:Build     -p:Configuration=Release -v:minimal
done
```

Each build should report 0 errors. The compiled `.cto` (under each project's `obj/.../*.cto`) embeds the new command id — open one and confirm `cmdFormatStyles` appears.

## Manual smoke test (post-install)

For each host you can install the extension into (`doc/deployment.md` has install paths + MEF cache clearing):

1. Build the host project (above).
2. Install into the appropriate Extensions directory.
3. Clear MEF / component cache (per `doc/deployment.md`).
4. Open the host.
5. **Tools menu → "Format Styles…"** should appear.
6. Click → `FormatStylesEditorWindow` opens with the three-column editor.
7. Close the host cleanly.

---

## Risks

| Risk | Mitigation |
|---|---|
| VSCT cross-contamination (all hosts looking for the last-built project's `.cto`) | Build per-project with MSBuild as scripted above; never build the solution. |
| Wrong parent group for SSMS — `IDG_VS_MM_TOOLSADDINS` is invisible in SSMS | Use `IDG_VS_TOOLS_EXT_TOOLS` for SSMS 20/21/22 per CLAUDE.md. |
| Command id collision with spec-019's reserved range | Take `0x0916` and tighten the reservation comment to `0x0917..0x093F`, OR open a fresh range at `0x1000`. |
| Silent miss of one of the 6 hosts — extension loads, no menu entry | Build + manually smoke-test each host. |
| `FormatStylesEditorWindow.Launch()` throws because of theme / DTE initialisation order | Wrap `Execute` in try/catch + MessageBox (the template above already does this). |

---

## Closing the task

When all 6 hosts are wired + verified, update `specs/020-sqlprompt-visual-parity/tasks.md`:

```markdown
- [X] T059 [US3] Wired Format Styles command across all 6 hosts. Shared command id `CommandIds.CmdFormatStyles = 0x0916`; shared launcher `FormatStylesCommand` in `src/AkmlSql.Shell.Shared/Commands/`. Each host's VSCT places the button under its Tools-equivalent group (SSMS: `IDG_VS_TOOLS_EXT_TOOLS`; VS: `AkmlSqlMenuGroup` / `IDG_VS_MM_TOOLSADDINS`). Each host's package class invokes `FormatStylesCommand.Initialize(this, commandService)`. See PR #XXX.
```
