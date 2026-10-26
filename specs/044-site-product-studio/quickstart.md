# Validation

1. Run Release `dotnet test tests/AkmlSql.Site.Tests/AkmlSql.Site.Tests.csproj`.
2. Run `scripts/generate-theme-css.ps1 -RepoRoot . -CheckOnly`, then with `-OutputFolder src/AkmlSql.Site/wwwroot/css/themes`.
3. Restore/build `AKML-SQL.slnx` in Release using full Visual Studio MSBuild.
4. Publish Site to a fresh ignored artifact folder. Start on loopback with isolated Analytics:DatabasePath/Downloads:Folder/Site:BaseUrl, disabled client errors and empty admin/mail config. Do not run deployed E2E fixtures.
5. Check 320/390/768/1440/1920 px and light/dark/high contrast: no overflow, broken images or CSP/page errors. Capture desktop/mobile evidence.
6. Select each tour item, open viewer, move in gallery, Escape and verify focus. Open FAQ, toggle theme, navigate to Features/Download/docs and back. Theme persists.
7. JS-disabled: all examples/native links remain available. Check enlarged browser text and reduced motion.

Record results/limitations in baseline.md; stop the local preview after checks.
