# Quickstart — AKML SQL Web Edition

End-user-oriented walkthrough for the first usable web surface (M2 onwards). Companion to the installer-component contract and the pairing-flow contract.

---

## 1. Install (M4)

1. Download `AKMLSQLSetup.exe`.
2. Run the installer. On the **Components** page, check **Web edition (local)**.
3. Choose hosting:
   - **Host on local IIS** (recommended) — the installer creates an IIS site for you.
   - **Don't host** — the WASM bundle is placed on disk and you can serve it from any static host.
4. Choose network exposure:
   - **Localhost only** (default) — only this machine can browse.
   - **LAN exposed** — other machines on your network can browse. A self-signed TLS certificate is generated and a one-time pairing PIN is shown on the success page.
5. Pick the engine bridge port (default `47291`).
6. Click **Install**.
7. On the success page, click **Open in browser**.

For unattended installs, see the silent-install flags in `contracts/installer-component.md`.

---

## 2. First open — formatter and analyser only (no engine required)

When the browser loads the web edition the first time:

1. The editor opens with a small "paste your SQL here" prompt.
2. Paste a `.sql` script (or use **Open file…** to load one from disk).
3. Click **Format** (or press `Ctrl+K, Ctrl+F`). The script is reformatted using the **Default** profile.
4. Click **Analyse** (or press `Ctrl+K, Ctrl+L`). The problems panel populates with findings; click any row to jump to the line.

Everything in this step runs entirely in your browser — no engine, no SQL Server. This is the M2 deliverable.

### Importing a profile from the IDE plugin

1. Open **Settings → Formatting profiles**.
2. Click **Import** and pick a `.akmlstyle` or `.sqlpromptstylev2` file from your machine.
3. The profile appears in the picker; switching to it applies it on the next Format.

### Theme

Settings → Theme:

- **Match system** (default) — follows your OS Light/Dark preference.
- **Light / Dark / High contrast** — explicit override.

---

## 3. Pair with a local engine for live IntelliSense (M3)

If you installed only **Localhost only**, this happens automatically: the web edition detects the engine on `127.0.0.1` and lights up live schema features. You'll see the **Live** status badge in the footer.

If you installed **LAN exposed**, or you're browsing from another machine:

1. Click the connection picker in the top right and choose **Add connection**.
2. Enter:
   - **Host** — e.g. `dev-host.local`
   - **Port** — the value you chose at install time (default `47291`)
   - **Pairing PIN** — copy from the installer success page or from `INSTALL-SUMMARY.txt` on the engine host
3. Click **Pair**. The browser performs a WSS handshake, the engine mints a long-lived bearer token, and the connection is saved.
4. The first time you connect, the browser shows a **Trust this engine?** prompt with the TLS certificate fingerprint. If it matches the value on the install summary, click **Trust**.

After pairing, the connection picker shows **Connected — Live**. Completions, signature help, and goto-definition now reflect real schema.

---

## 4. Offline / caching (M5)

Once you've used the web edition against a database with a live engine, schema is cached locally. To verify:

1. With a live engine, open a query against a database. Completions appear; the status badge shows **Live**.
2. Stop the engine (or disconnect from the network if you're on LAN).
3. Keep typing in the editor. Completions still appear, and the badge changes to **Cached** with the timestamp of the last refresh.
4. Restart the engine. The badge silently switches back to **Live** after a background refresh.

To clear cached schema:

- **Settings → Schema cache** — see the list of cached databases, sizes, and last-used times.
- Click **Clear all** or **Clear** next to a specific database.

The cache is keyed by the SQL Server's canonical identity, so connecting via a different alias (DNS, IP, FQDN) to the same SQL Server reuses the same cache entry.

---

## 5. AI assistance with your own provider key (M6)

1. Open **Settings → AI**.
2. Choose a provider (Claude, OpenAI, Gemini, Azure OpenAI, Ollama, LM Studio).
3. Paste your provider API key. The key is wrapped at rest using browser-native cryptography — it is never stored in plain.
4. Optionally choose a model and (for Azure / Ollama / LM Studio) the endpoint URL.

From the editor:

- Select SQL and click **Explain** — plain-English explanation.
- Click **Text → SQL** and type a natural-language description.
- Click **Fix** / **Optimize** on the current document or selection.

All AI requests go directly from your browser to the provider — no AKML server is involved.

To remove a key:

- Settings → AI → choose provider → **Remove key**. The wrapped bytes are zeroised before the record is deleted.

---

## 6. Export diagnostics (introduced in M2)

If something doesn't work:

1. Open **Settings → Diagnostics**.
2. Click **Export diagnostics**. A `akmlsql-web-diagnostics-<timestamp>.zip` downloads.
3. Send the zip to support. It contains:
   - `browser.log.json` — a ring-buffer of formatter/analyser/bridge/AI/cache/UI events from this browser session.
   - `engine.log` — present only if the engine bridge was reachable when you clicked Export.

The export bundle never leaves your machine until you explicitly send it.

---

## 7. Regenerate the pairing PIN

If you lose track of pairings or share the PIN by mistake:

1. On the engine host, open the engine UI (taskbar tray icon → **Pairing**).
2. Click **Regenerate PIN**. A fresh 6-digit PIN is shown.
3. Click **Revoke all** to disconnect every paired browser; future browsers must re-pair with the new PIN.

---

## 8. Uninstall

- Re-run the installer and **uncheck** *Install web edition* — the web edition is removed, the IDE plugins are preserved.
- Or go to **Add or remove programs → AKML SQL → Modify → Web edition → Uninstall**.
- The installer asks whether to also remove `%AppData%/AKML SQL Web/` (settings, tokens, engine logs). Default is **No**.

---

## Verifying acceptance scenarios from the spec

| User story | How to verify here |
|------------|---------------------|
| US1 — Format and lint (P1) | Sections 1–2 |
| US2 — Live IntelliSense (P2) | Section 3 |
| US3 — One-click deploy (P3) | Section 1 |
| US4 — Offline IntelliSense (P4) | Section 4 |
| US5 — AI assistance (P5) | Section 5 |
| FR-005a — Diagnostics | Section 6 |
| FR-014 — Token revocation | Section 7 |
| SC-007 — Plugins untouched on uninstall | Section 8 |

---

## Where to read more

- Architecture and milestone-by-milestone scope: `doc/WEB/00-INDEX.md`
- Spec and clarifications: `specs/021-web-edition/spec.md`
- Implementation plan: `specs/021-web-edition/plan.md`
- Bridge handshake contract: `specs/021-web-edition/contracts/rpc-handshake.md`
- AI key wrapping contract: `specs/021-web-edition/contracts/ai-key-wrapping.md`
