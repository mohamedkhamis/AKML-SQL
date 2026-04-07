#!/bin/bash
# Hot-swap AKML SQL DLLs for SSMS 22 without running the installer.
# Requires:
#   - SSMS 22 must be CLOSED (locked DLLs otherwise)
#   - Run from elevated bash (admin) — Program Files writes need admin
#   - Engine + SSMS 22 must already be built/published

set -e

REPO="D:/Repo/01-Khamis-Projects/AKML-SQL"
EXT_DIR="/c/Program Files/Microsoft SQL Server Management Studio 22/Release/Common7/IDE/Extensions/AkmlSql"
ENGINE_DIR="/c/Program Files (x86)/AKML SQL/Engine"
APP_DIR="/c/Program Files (x86)/AKML SQL"
MEF_CACHE="/c/Users/MohamedKhamis/AppData/Local/Microsoft/SSMS/22.0_*/ComponentModelCache"

# 1. Safety check: refuse if SSMS or Engine is running
if tasklist 2>/dev/null | grep -qi "ssms.exe\|akmlsql.engine.exe"; then
    echo "ERROR: SSMS or AkmlSql.Engine is running. Close SSMS first."
    tasklist 2>/dev/null | grep -i "ssms\|akml"
    exit 1
fi

echo "==> Copying SSMS 22 shell DLLs..."
SRC_SHELL="$REPO/src/AkmlSql.Ssms22/bin/Release/net472"
cp -v "$SRC_SHELL/AkmlSql.Ssms22.dll" "$EXT_DIR/"
cp -v "$SRC_SHELL/AkmlSql.Core.dll" "$EXT_DIR/"
# Copy any pdb files alongside (optional, helps debugging)
[ -f "$SRC_SHELL/AkmlSql.Ssms22.pdb" ] && cp -v "$SRC_SHELL/AkmlSql.Ssms22.pdb" "$EXT_DIR/" || true
[ -f "$SRC_SHELL/AkmlSql.Core.pdb" ] && cp -v "$SRC_SHELL/AkmlSql.Core.pdb" "$EXT_DIR/" || true

echo
echo "==> Copying Engine binaries..."
SRC_ENGINE="$REPO/src/AkmlSql.Engine/bin/Release/net10.0/win-x64/publish"
cp -rv "$SRC_ENGINE"/* "$ENGINE_DIR/"

echo
echo "==> Copying Core/Updater/Analyzer to {app}..."
cp -v "$SRC_SHELL/AkmlSql.Core.dll" "$APP_DIR/"
cp -v "$REPO/src/AkmlSql.Updater/bin/Release/net10.0/win-x64/publish/AkmlSql.Updater.exe" "$APP_DIR/"
cp -v "$REPO/src/AkmlSql.Analyzer/bin/Release/net10.0/win-x64/publish/AkmlSql.Analyzer.exe" "$APP_DIR/"

echo
echo "==> Clearing SSMS 22 ComponentModelCache (forces MEF re-discovery)..."
for cache in $MEF_CACHE; do
    if [ -d "$cache" ]; then
        rm -rf "$cache"
        echo "Removed: $cache"
    fi
done

echo
echo "==> Done. Start SSMS 22 — first launch will rebuild MEF cache (slight delay)."
