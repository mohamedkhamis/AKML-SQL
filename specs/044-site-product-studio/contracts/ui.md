# Public UI contract

- `/`: Product Studio concept A; “Good SQL. Less friction.” hero.
- Primary CTA `/download`; secondary `#product-tour`. Browser setup `/docs/topics/browser-setup`.
- SQL Server: SSMS 22/browser. PostgreSQL: browser with enabled engine.
- `.studio-tour-button[aria-controls]` maps to `.studio-tour-panel` ID. Selector begins hidden. Script reveals it, hides inactive panels, sets active aria-pressed=true. Normal Tab/Enter/Space behavior.
- ProductScreenshot links retain actual image hrefs and themed variants. Existing viewer owns modal/gallery/keyboard/focus behavior.
- No-JS: all examples and core links remain available. No new CSP exemptions, endpoint changes or persistent tour state.
- Existing theme, release, documentation, feedback and consent contracts remain.
