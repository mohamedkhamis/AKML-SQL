# Contract: Bundle-size measurement protocol

**Owner**: User Story 5 (FR-018–FR-022)
**Location of output**: `specs/021-web-edition/M2-BUNDLE-SIZE.md` (replaces the existing placeholder).

The measurement is a four-step procedure. The audit document records each step's output so a second reviewer can re-run it on a matching host and reproduce the number.

---

## Step 1 — Verify the host

A bundle measurement is only valid on a host that produces real release artefacts: Windows 11 with the full .NET SDK and the WebAssembly tooling. The audit document records:

- OS version (e.g. `Windows 11 Pro N, 10.0.26220, x64`)
- `dotnet --version` (e.g. `11.0.100-preview.4.26230.115`)
- `dotnet workload list` excerpt showing `wasm-tools` (or `wasm-tools-net10` for spec-023 hosts) installed
- Master commit hash (`git rev-parse HEAD`)

If any line is missing, the audit is invalid and the measurement cannot be cited.

---

## Step 2 — Publish

```powershell
dotnet publish src/AkmlSql.Web/AkmlSql.Web.csproj -c Release -nologo
```

The audit document records the exact command — including any `-p:` overrides — and the captured exit code (must be 0).

**No `-p:RunAOTCompilation=true`** for the M2 baseline. AOT is a per-asset decision deferred from spec 023 §5; the M2 baseline measures the interpreted bundle so M3's growth can be tracked against a consistent reference point. AOT measurements (if needed) go in a separate row.

---

## Step 3 — Verify Brotli is active

For every relevant asset under `src/AkmlSql.Web/bin/Release/net10.0/publish/wwwroot/_framework/`, assert a sibling `.br` file exists:

```powershell
$framework = 'src/AkmlSql.Web/bin/Release/net10.0/publish/wwwroot/_framework'
$missing = Get-ChildItem $framework -Recurse -Include *.dll, *.wasm, *.dat, *.js, *.pdb |
    Where-Object { -not (Test-Path "$($_.FullName).br") }
if ($missing) { throw "Brotli sibling missing for: $($missing -join ', ')" }
```

The audit document records `Brotli confirmed active: yes` only if this script exits cleanly (no missing siblings). If it fails, the audit is invalid until the host's toolchain is fixed.

---

## Step 4 — Sum the compressed total

```powershell
$total = (Get-ChildItem $framework -Recurse -Filter *.br | Measure-Object -Property Length -Sum).Sum
Write-Host ("Compressed _framework total: {0:N2} MB" -f ($total / 1MB))
```

Audit document records:

- The compressed total in MB (one decimal)
- A sorted per-asset breakdown (descending by size): each row is `<filename>.br` + size in KB
- The top 5 largest assets explicitly called out

---

## Verdict

Compare against the M1 decision document's compressed-total target (e.g. `≤ 25 MB compressed`; the actual number is whatever `docs/m1-wasm-decision.md` records).

| Verdict | Condition | Required next step |
|---------|-----------|--------------------|
| `WITHIN_TARGET` | `compressed_total ≤ m1_target` | Record headroom = `m1_target - compressed_total` MB; record next-checkpoint trigger ("M3 must re-measure before merge") |
| `OVER_TARGET` | `compressed_total > m1_target` | Identify the largest single asset; record a lazy-loading plan; apply the plan to `src/AkmlSql.Web/` and re-measure; do not commit the audit document with the `OVER_TARGET` verdict |

The audit document MUST end with the verdict line. A trailing `OVER_TARGET` verdict that does not also carry an applied lazy-loading plan is rejected as incomplete (FR-021).

---

## Validation checklist

- [ ] §1 (host) lists the four required lines: OS, `dotnet --version`, workload list excerpt, master commit
- [ ] §2 (publish) records the exact command and exit code 0
- [ ] §3 (Brotli) records `Brotli confirmed active: yes` AND the script exits cleanly
- [ ] §4 (sum) records the compressed total in MB and the per-asset breakdown with top 5 called out
- [ ] §5 (verdict) is `WITHIN_TARGET` (with headroom + next checkpoint) or `OVER_TARGET` (with lazy-loading plan applied)
- [ ] The committed document never carries `OVER_TARGET` without the plan applied
