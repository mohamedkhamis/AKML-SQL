# M2 Bundle Size Audit — DEFERRED

**Status: deferred** (spec 021 T054). Needs a production publish on a Windows host so the trimmer + Brotli compressor run.

## What the audit gates

The M1 plan target is a **WASM payload under 10 MB compressed** for the M2 surface. Once we cross that line, the next step is moving the analyser rule packs (the largest single contributor) behind a lazy-load that fires on first **Analyse** click.

## Procedure

```bash
# From a Windows / Linux host with the .NET 10 SDK installed:
dotnet publish src/AkmlSql.Web/AkmlSql.Web.csproj -c Release

# The published output lands under bin/Release/net10.0/publish/wwwroot/_framework/.
# Tabulate every file ending in .wasm / .dll / .blat / .br with its byte size.
ls bin/Release/net10.0/publish/wwwroot/_framework/ -l
```

Record the totals in this file as a Markdown table:

| File group | Uncompressed | Brotli (.br) | Delta vs M1 target |
|------------|--------------|--------------|--------------------|

## Tracked thresholds

- **dotnet.wasm**: framework — out of our control; we record it for context.
- **AkmlSql.Analysis.dll**: every M2 release should hold this stable or shrink (rule pack growth is the usual culprit).
- **AkmlSql.IntelliSense.dll**: stable through M3 (M5 introduces growth when DatabaseCache grows).
- **AkmlSql.AI.dll**: relevant only when M6 lands.

## Why deferred

Cannot run a Release publish inside the headless CLI session that produced the M2 code — the publish chain pulls a JIT-AOT compiler that requires the full Windows SDK. Lands when a workstation session can run `dotnet publish` and screenshot the output.
