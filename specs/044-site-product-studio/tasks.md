# Tasks: Product Studio

## Setup and foundation

- [x] T001 Record approved scope, research and contract in specs/044-site-product-studio/.
- [x] T002 Add palette tokens in docs/theme-tokens.json and regenerate Site/Web themes.
- [x] T003 Extend src/AkmlSql.Site/Components/ProductScreenshot.razor for actual browser captures and add provenance in specs/044-site-product-studio/assets.md.

## US1 — Value and database choice

- [x] T004 [US1] Update acceptance coverage in tests/AkmlSql.Site.Tests/Components/HomePageTests.cs for support, release fallback and setup paths.
- [x] T005 [US1] Implement benefits, database sections, setup and FAQ in src/AkmlSql.Site/Components/Pages/Home.razor.
- [x] T006 [US1] Align public introductory copy in Components/Pages/Features.razor, Download.razor, Components/HostSupportNotice.razor and Layout/MainLayout.razor.
- [x] T007 [US1] Add accurate public setup instructions in doc/topics/browser-setup.md.

## US2 — Product evidence

- [x] T008 [US2] Implement progressive tour in wwwroot/js/product-tour.js and Home.razor, reusing screenshot-viewer.js.
- [x] T009 [US2] Validate selector, gallery, keyboard focus and no-JS fallbacks in specs/044-site-product-studio/verify.cjs and update superseded homepage E2E expectation.

## US3 — Selected design

- [x] T010 [US3] Integrate responsive approved layout in wwwroot/css/product-studio.css and Components/App.razor; preserve theme rules.
- [x] T011 [US3] Capture and validate themes/widths/navigation in specs/044-site-product-studio/verify.cjs.

## Final verification

- [x] T012 Run Site tests, theme gates and full MSBuild restore/build; record specs/044-site-product-studio/baseline.md.
- [x] T013 Update PROJECT_MEMORY.md, CLAUDE.md and doc/progress.md with delivered behavior and validation.

Dependencies: T001 → T002/T003 → T004/T005 → T006/T007/T008 → T009/T010/T011 → T012/T013. US1 is the smallest meaningful increment. Asset preparation and isolated research may run independently; edits/builds are coordinated. All stories are delivered in this task.

Setup tooling note: setup-plan used explicit SPECIFY_FEATURE to avoid the current branch's feature; setup-tasks references a missing Resolve-TemplateContent helper. The checked-in task template was read directly; no tooling repair is included. No extension hooks configured.
