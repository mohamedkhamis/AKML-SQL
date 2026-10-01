# Getting Started

AKML SQL is a SQL development plugin for SQL Server Management Studio (SSMS) 22. It adds IntelliSense, SQL formatting, static code analysis, refactoring, snippets, query history, and optional AI assistance to the query editor you already use. AKML SQL is free and open source under the MIT license.

## Requirements

- Windows 10 or later
- SQL Server Management Studio 22

> Visual Studio 2026 is no longer supported. Upgrading removes the AKML SQL extension that earlier versions added to Visual Studio.
- A SQL Server database to connect to

## Install AKML SQL

1. Download `AKMLSQLSetup.exe` from the project page: https://github.com/mohamedkhamis/AKML-SQL
2. Run the installer. If SSMS is open, it offers to close it for you.
3. Accept the license agreement.
4. On the environment scan screen, check that SQL Server Management Studio 22 is ticked. (Untick it to install only the web edition.)
5. Click **Install**, then **Finish**.
6. Start SSMS. AKML SQL loads automatically.

Re-running the installer over an existing installation upgrades it in place. Your settings, styles, and snippets are kept.

## Find the commands

AKML SQL adds its own **AKML SQL** menu to the SSMS 22 menu bar. Format Document, Format Selection, Active Style and SQL History sit at the top; everything else is grouped under **Formatting**, **Refactor**, **Navigate**, **Tabs**, **AI** (shown when AI is turned on), **Tools** and **Help**.

Many features also appear on the editor right-click menu, and the Command Palette (**Ctrl+Shift+P**) finds any command or option by name.

## Open the Options dialog

1. Open **AKML SQL** -> **Options…**.
2. Pick a page in the tree on the left, or type in the search box (**Ctrl+E**) to jump to a setting.

Each feature area (suggestions, formatting, snippets, code analysis, refactoring, AI) has its own page; [Options](options.md) describes every page. Settings are stored in `%AppData%\AKML SQL\config.json` and apply immediately. See the [Configuration reference](../configuration.md) for every setting.

## Check for updates

- AKML SQL checks for updates automatically on startup (you can turn this off in Options).
- To check manually, use **AKML SQL** -> **Help** -> **Check for Updates**.
- If a newer version exists, a notification bar appears with a download link.

## Next steps

- [Connect to SQL Server](connecting.md)
- [Write queries faster with IntelliSense](intellisense.md)
- [Format your SQL](formatting.md)
- [Fix problems with Troubleshooting](troubleshooting.md)
