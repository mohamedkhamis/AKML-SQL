# Contract: Site Routes & Page Behavior

The site's public interface is its URL surface. All pages render via static SSR (full HTML, no interactivity required to read content). Enhanced navigation is enabled; 404s use .NET 10 `NavigationManager.NotFound()` semantics.

## Routes

| Route | Page | Content | Key requirements |
|-------|------|---------|------------------|
| `/` | Home | Product name, one-line value proposition, feature highlights, prominent download CTA | FR-001; download reachable within 2 clicks (SC-001) |
| `/features` | Features | FR-002 capability areas: IntelliSense, formatting, static analysis, refactoring, snippets, SQL history, AI assistance — short descriptions, screenshots where available (deferred loading) | FR-002 |
| `/download` | Download | Latest release (version, date, supported hosts) + installer link + SHA-256; older releases listed below | FR-003; data from [releases-json.md](releases-json.md) |
| `/docs` | Docs index | Section tree of all documents; empty-state message when the content source is empty | FR-004, FR-007; spec edge cases |
| `/docs/{slug}` | Doc page | Rendered document (headings, lists, tables, links, images, highlighted code); sidebar tree + title filter + search box | FR-005–FR-007; slug rules in [docs-content.md](docs-content.md) |
| `/docs/{slug}` (unknown) | NotFound | Friendly 404, link back to `/docs` | .NET 10 `NotFound` |
| any unknown | NotFound | Friendly 404 | — |
| `/support` | **Reserved — not implemented** | Nav/footer slot reserved for the future "Support / Fund me" section; no route registered, no payment/donation UI anywhere | FR-010 |

## Header navigation (all pages)

`Home · Features · Docs · Download` (+ repo link, FR-011) — with an obvious insertion point for the future Support entry (FR-010). Keyboard navigable (SC-006).

## Cross-page requirements

- **SEO/social**: every page sets `<title>`, meta description, Open Graph tags; `sitemap.xml` + `robots.txt` served (includes all doc routes).
- **Responsive**: no horizontal scroll, readable text at 360–1920 px (FR-008, SC-003).
- **Theming**: dark palette default (FR-009); optional JS toggle follows `prefers-color-scheme` for light; choice persisted (localStorage); no flash-of-wrong-theme (inline boot script, same pattern as `akml-theme-boot.js`).
- **Performance**: primary content in first HTML paint; images/screenshots lazy-loaded; MiniSearch + index deferred until `/docs*` interaction (SC-004, slow-network edge case).
- **Accessibility**: semantic landmarks (`header/main/nav/footer`), skip link, focus visible, contrast per dark palette tokens (SC-006).
- **Footer**: product name, license (MIT) link, source repository link (FR-011), reserved Support slot.
