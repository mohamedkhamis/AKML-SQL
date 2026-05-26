# M2 Bundle Size Audit

**Status**: ✓ AUDIT PASSES — **WITHIN_TARGET**
**Closed by**: spec 024 (US5, T033–T037)

- Date: 2026-05-26
- Capturer: Mohamed Khamis
- Master commit: `a371be24af2a3ecb936ff394b86b01a732bfdb08` (`024-m2-web-closure`)
- Web edition build: post-spec-024 foundation (Phase 1 + 2 of `specs/024-m2-web-closure/tasks.md`)

## 1 — Host environment

| Item | Value |
|------|-------|
| OS | Windows 11 Pro N, 10.0.26220, x64 |
| `dotnet --version` | `11.0.100-preview.4.26230.115` (.NET 11 preview SDK) |
| WebAssembly workload | `wasm-tools 11.0.100-preview.4.26230.115` installed — note: `wasm-tools-net10` is the variant recommended for `net10.0` targets on an 11-preview SDK (see spec 023 §5); without it, the publish runs without relinking. The compressed total below is therefore an **upper bound** of what a fully relinked publish would produce. |
| Brotli confirmed active | **Yes** — 122 of 122 relevant `_framework/*` files have sibling `.br` artefacts (366 total files = 122 raw + 122 Brotli + 122 Gzip; 1:1:1 layout) |
| `git rev-parse HEAD` | `a371be24af2a3ecb936ff394b86b01a732bfdb08` |

## 2 — Publish command + exit code

```powershell
dotnet publish src/AkmlSql.Web/AkmlSql.Web.csproj -c Release --nologo
```

Exit code: **0**. Build emitted the standard "Publishing without optimizations" hint because `wasm-tools-net10` was not installed; the publish nevertheless produces a valid bundle with Brotli + Gzip artefacts.

## 3 — Brotli verification

Every relevant asset under `src/AkmlSql.Web/bin/Release/net10.0/publish/wwwroot/_framework/` has a sibling `.br` file (and a `.gz` for legacy fallback). PowerShell check:

```powershell
$framework = 'src/AkmlSql.Web/bin/Release/net10.0/publish/wwwroot/_framework'
$missing = Get-ChildItem $framework -Recurse -Include *.dll, *.wasm, *.dat, *.js, *.pdb |
    Where-Object { -not (Test-Path "$($_.FullName).br") }
$missing.Count   # → 0
```

**Brotli confirmed active: yes.**

## 4 — Compressed total + per-asset breakdown

**Compressed `_framework/*.br` total: 7,178,793 bytes ≈ 6.85 MB** (122 files).

For reference, the same publish produces:

| Series | Bytes | MB | Files |
|--------|-------|----|-------|
| Brotli (`*.br`) | 7,178,793 | **6.85** | 122 |
| Gzip (`*.gz`) | 9,078,001 | 8.66 | 122 |
| Uncompressed | 28,385,671 | 27.07 | 122 |

### Top-5 largest `.br` assets

| Asset | Size (KB) |
|-------|----------|
| `dotnet.native.53ez3dx5uy.wasm.br` | 953 |
| `System.Private.CoreLib.72grlutif7.wasm.br` | 561 |
| `System.Private.CoreLib.qhwrojdev3.wasm.br` | 561 |
| `Microsoft.ML.Tokenizers.Data.Cl100kBase.hvq1te7tk7.wasm.br` | 515 |
| `dotnet.native.90fn5xofzy.wasm.br` | 418 |

Notable AI-side payloads (M6 territory but already shipped via spec 021): `Microsoft.SqlServer.TransactSql.ScriptDom.*.wasm.br` 344 KB, `OpenAI.*.wasm.br` 305 KB, `System.Private.Xml.*.wasm.br` 176 KB, `Mscc.GenerativeAI.*.wasm.br` 173 KB, `MessagePack.*.wasm.br` 165 KB.

## 5 — Verdict

| Item | Value |
|------|-------|
| Compressed total | 6.85 MB |
| M1 target | ≤ 25 MB (`docs/m1-wasm-decision.md` §1 Q3) |
| **Verdict** | **`WITHIN_TARGET`** |
| Headroom | ~18.15 MB before the M1 ceiling |
| Growth since M1 baseline | M1 measured 4.83 MB after a fully relinked publish (`wasm-tools-net10` active). Today's 6.85 MB is +2.02 MB. Of that, ~1 MB is attributable to the missing relink (uncompressed grew from 20.69 → 27.07 MB without the trimmer pass); the remaining ~1 MB is new code shipped between M1 and now (AI providers OpenAI / Gemini, MessagePack, ScriptDom growth). |

## 6 — Next checkpoint

**M3 must re-measure before merge.** M3 adds WebSocket transport, schema-cache sync, and live IntelliSense — all of which grow the bundle. If M3's measurement crosses the M1 ceiling (≤ 25 MB), the largest single asset (currently `dotnet.native.*.wasm.br` at ~1.4 MB combined across both variants) becomes the first lazy-load candidate per spec 024 FR-021.

If a maintainer re-runs this audit with `wasm-tools-net10` installed, the compressed total is expected to drop back to the ~4.83 MB neighbourhood; record that measurement here as an addendum rather than overwriting this baseline (the larger number is the conservative bound and the realistic upper limit on dev machines without the workload variant).
