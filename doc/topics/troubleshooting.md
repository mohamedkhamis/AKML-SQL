# Troubleshooting

Most problems come down to a stale IDE cache, a stopped engine process, or a missing setting. Work through the sections below in order.

## Check the logs first

AKML SQL writes daily log files to:

```
%AppData%\AKML SQL\logs\
```

Open the newest file and search for `Error`. You can also use **AKML SQL** -> **View Logs**. For more detail, set `logMinimumLevel` to `"Debug"` or `"Verbose"` in `config.json` and restart the IDE.

## Where settings live

```
%AppData%\AKML SQL\config.json
```

The file is created with defaults on first run and written safely (temp file plus rename), so it should not corrupt. If you break it while editing by hand, delete it — a fresh default file is created on next start. Every key is documented in the [Configuration reference](../configuration.md).

## Update checking

- AKML SQL checks for updates on startup unless you turned this off; check manually with **AKML SQL** -> **Check for Updates**.
- The result of the last check is cached under `%AppData%\AKML SQL\` and shown as a notification bar.
- "Could not check for updates" means the machine could not reach the network.

## After an SSMS or Visual Studio update

IDE updates can leave a stale component cache, which stops extensions from loading. Clear the cache and restart:

- SSMS 22: delete `%LocalAppData%\Microsoft\SSMS\22.0_*\ComponentModelCache\`
- VS 2026: delete `%LocalAppData%\Microsoft\VisualStudio\18.0_*\ComponentModelCache\`

```powershell
#Clear all SSMS 22 caches (PowerShell)
Remove-Item "$env:LOCALAPPDATA\Microsoft\SSMS\22*\ComponentModelCache" -Recurse -Force
```

Do the same after installing or upgrading AKML SQL if the menu does not appear.

## The AKML SQL menu is missing

1. Clear the component cache as above and restart the IDE.
2. Check the IDE's `ActivityLog.xml` (under `%AppData%\Microsoft\SSMS\22.0_*\` or `%AppData%\Microsoft\VisualStudio\18.0_*\`) for load errors.
3. Verify the extension files exist in the IDE's Extensions folder — for SSMS 22 they must be under the `Release\Common7\IDE\Extensions\AkmlSql\` subfolder.
4. Re-run the installer; it repairs in place and clears the caches for you.

## IntelliSense or schema features not working

1. Check Task Manager for `AkmlSql.Engine.exe` — the helper process that powers completions. If it is absent, look at the logs.
2. Confirm IntelliSense is enabled in **Tools** -> **Options** -> **AKML SQL** -> **IntelliSense**.
3. If the built-in SSMS IntelliSense fights the AKML one, enable "Disable native IntelliSense" on that page.
4. If the schema never loads, confirm your login has `VIEW DATABASE STATE` and `VIEW ANY DEFINITION`, then refresh manually via **Tools** -> **AKML SQL** -> **Refresh Schema Cache**.

## Report an issue

Open an issue on GitHub: https://github.com/mohamedkhamis/AKML-SQL/issues

Include: your SSMS/VS version, the AKML SQL version (from **AKML SQL** -> **About**), what you did, what happened, and the relevant lines from the log file. Logs never contain passwords or API keys, but review them before attaching. You can also use the **Send Feedback** command in the AKML SQL menu.
