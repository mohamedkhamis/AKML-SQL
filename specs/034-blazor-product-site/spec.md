# Feature Specification: AKML SQL Product Website (Blazor)

**Feature Branch**: `034-blazor-product-site`  
**Created**: 2026-08-26  
**Status**: Draft  
**Input**: User description: "Create new project Blazor — product site for AKML SQL with documents and download of AKML SQL; documentation must be automatically added when a new feature is added; future 'fund me' section (not now); user wants to choose from style options."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Discover the Product and Download It (Priority: P1)

A visitor lands on the AKML SQL product site, reads a clear overview of what the product does (AI-powered SQL development assistance for SSMS and Visual Studio), browses the headline features (IntelliSense, formatting, static analysis, snippets, AI assistance), and downloads the latest installer for their environment.

**Why this priority**: A product site that does not convince visitors and let them download the product delivers no value. This is the core purpose of the site and is a viable MVP on its own.

**Independent Test**: Can be fully tested by opening the site home page, navigating to the download page, and starting a download of the latest release — delivering the value of product discovery and acquisition without any documentation features.

**Acceptance Scenarios**:

1. **Given** a first-time visitor on the home page, **When** they scroll the landing section, **Then** they see the product name, a one-line value proposition, the top feature highlights, and a prominent download call-to-action.
2. **Given** a visitor on the download page, **When** they view available releases, **Then** they see the latest version number, release date, supported hosts (SSMS 22 / VS 2026), and a working download link for the installer.
3. **Given** a visitor who clicked download, **When** the download completes, **Then** they see installation/getting-started guidance or a link to it.

---

### User Story 2 - Browse Automatically Maintained Documentation (Priority: P2)

A visitor opens the documentation section and finds up-to-date docs for every shipped feature — including the most recently added ones — without the site owner having to manually wire each new document into the site.

**Why this priority**: Documentation drives adoption and reduces support burden, but only if it stays current. Automatic ingestion of new feature documents is the user's explicitly stated must-have, and it is what makes the docs section trustworthy over time.

**Independent Test**: Can be fully tested by adding a new documentation file to the docs content source, refreshing the site, and verifying the new document appears in the documentation navigation and renders correctly — without any code or configuration change.

**Acceptance Scenarios**:

1. **Given** the documentation section, **When** a visitor opens it, **Then** they see an organized, navigable list/tree of all documentation topics with titles.
2. **Given** a documentation file was newly added to the docs content source, **When** the site is rebuilt or refreshed, **Then** the new document automatically appears in the documentation navigation and is readable — with no manual registration step.
3. **Given** a visitor reading a document, **When** the document contains headings, code blocks, tables, or images, **Then** they render readably with correct formatting and syntax highlighting.
4. **Given** many documents exist, **When** a visitor searches or filters the docs, **Then** matching documents are listed.

---

### User Story 3 - Consistent Branded Visual Experience (Priority: P3)

A visitor experiences a cohesive, professional visual style across all pages (landing, features, docs, download) on desktop and mobile, matching the design direction the site owner selected.

**Why this priority**: Visual polish increases credibility and conversion, but the site is functional and valuable even before fine visual tuning; the chosen style direction is a prerequisite for final polish, ranked after core content flows.

**Independent Test**: Can be fully tested by viewing each page type (home, features, docs, download) at desktop and mobile widths and verifying consistent branding, readable typography, and responsive layout.

**Acceptance Scenarios**:

1. **Given** any page of the site, **When** viewed on desktop and on a mobile-width viewport, **Then** layout adapts without horizontal scrolling or unreadable text.
2. **Given** the owner selected the Developer Dark style, **When** any page is viewed, **Then** colors, typography, and component styling consistently reflect that dark, code-first style.
3. **Given** the Developer Dark style is the default, **When** a visitor's system preference or an optional toggle requests light mode, **Then** the site either follows it with an equivalent light palette or remains consistently dark by design.

---

### Edge Cases

- What happens when the download for the latest release is unavailable or the release feed fails? → Show a friendly fallback message and keep older releases listed.
- What happens when a newly added document has invalid formatting or missing title metadata? → The site still lists it using a derived title from the filename and renders what it can, without breaking the docs section.
- What happens when the docs content source is empty? → The docs section shows an empty-state message instead of an error.
- What happens with very long documents or deep navigation trees? → Navigation remains scrollable/collapsible and pages stay performant.
- How does the site behave on slow networks? → Core content (text, navigation) loads first; heavy assets are deferred.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The site MUST present a landing page with the product name, value proposition, key feature highlights, and a prominent call-to-action to download.
- **FR-002**: The site MUST present a features page/section describing the major capability areas (IntelliSense, formatting, static analysis, refactoring, snippets, SQL history, AI assistance) with short descriptions and screenshots where available.
- **FR-003**: The site MUST provide a download page listing the latest release (version, date, supported hosts) and offering the installer download; previous releases SHOULD remain accessible.
- **FR-004**: The site MUST provide a documentation section that renders documentation content from a designated docs content source (a folder of documentation files, e.g., Markdown).
- **FR-005**: The documentation section MUST automatically include newly added documentation files in its navigation and rendering — adding a file to the docs content source MUST be sufficient for it to appear on the site, with no per-document code or configuration change.
- **FR-006**: The documentation section MUST render headings, lists, tables, links, images, and syntax-highlighted code blocks readably.
- **FR-007**: The documentation section MUST provide navigation across documents (sidebar tree or index) and a way to locate content (search or filter).
- **FR-008**: The site MUST be responsive and usable on desktop and mobile viewports.
- **FR-009**: The site MUST implement one consistent visual style across all pages, using the owner-selected **Developer Dark** direction: dark background, glowing accent color, code-first aesthetic with monospace touches (GitHub Dark / VS Code feel), applied to landing, features, docs, and download pages alike.
- **FR-010**: The site MUST NOT include any payment, donation, or "fund me" functionality in this phase; the information architecture SHOULD leave an obvious place where a future "Support / Fund me" section can be added without restructuring.
- **FR-011**: The site SHOULD display product branding (name, logo/icon placeholder) and link back to the source repository and license information.
- **FR-012**: The site MUST be built as a Blazor web application project, as explicitly requested by the owner (hosting model is an implementation decision for planning).

### Key Entities *(include if feature involves data)*

- **Document**: A documentation entry rendered on the site. Key attributes: title, slug/route, source file, content body, category/section, ordering, last-updated date. Discovered automatically from the docs content source.
- **Release**: A downloadable product release. Key attributes: version number, release date, supported hosts (SSMS 22, VS 2026), download artifact(s), release notes summary.
- **Feature Highlight**: A marketed capability shown on the landing/features pages. Key attributes: title, short description, icon or screenshot.

## Assumptions

- **Docs content source**: The existing repository documentation (`doc/` Markdown files, and per-feature `specs/*/spec.md` where appropriate) is the content source. "Automatic" means the site discovers files at build or startup time — no database or manual registration. Exact inclusion rules (which folders feed the site) are decided during planning.
- **Release downloads**: Releases are produced by the existing build/release process; the site surfaces the latest published installer rather than building artifacts itself.
- **"Fund me"**: Explicitly deferred. Only structural room (a nav slot / footer area) is reserved now.
- **Branding assets**: No logo files are confirmed; a text/placeholder brand mark is acceptable for the first version.
- **Language**: Site content is in English, matching the product's existing documentation.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A first-time visitor can reach a working download of the latest release within 2 clicks from the home page, in under 1 minute.
- **SC-002**: 100% of documentation files present in the docs content source appear on the site automatically; adding a new documentation file makes it visible on the next site refresh/rebuild with zero manual steps, in under 5 minutes of owner effort.
- **SC-003**: Every page renders without layout breakage at viewport widths from 360 px to 1920 px.
- **SC-004**: Core pages load with primary content visible in under 3 seconds on a typical broadband connection.
- **SC-005**: 90% of evaluators (owner + peers) can find a specific feature's documentation via navigation or search on the first attempt.
- **SC-006**: All pages pass a basic accessibility sanity check (keyboard navigable, sufficient contrast for the selected style).
