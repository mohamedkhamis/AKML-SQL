# Feature Specification: Site Download Experience and Full Admin Portal

**Feature Branch**: `038-site-downloads-admin-portal`
**Created**: 2026-09-12
**Status**: Draft
**Input**: User description: "i want ehnace akml site for user ( focus on download versions ) it take a lot of time to start ( have bug ) , and allow me from admin setting ( add it ) to just show last versions only by setting , also admin i want to add metrix for me for example ciuntry of download and each person ( data related to pages visit is fine but not important for me ) for example i want individulas visits and also download akml sql and can group people by country and all data you can help me for example IP and so on ( i want full admin portal management )"

## Overview

The public AKML SQL site has two problems and one gap.

The **download page is slow and cluttered**: it advertises every release ever published (16 at the time of writing, ten of them from a single day), and a visitor waits noticeably before the page — and the download itself — gets going. The page is the site's primary call to action, so this is the most expensive defect the site has.

The **owner has no control over what the page shows**: the release list is whatever the published manifest contains. Trimming it means editing a file and redeploying.

The **owner cannot answer acquisition questions**: existing metrics count page views and downloads in aggregate, but cannot say who downloaded, from which country, or how a single visitor moved from arrival to installer. Country and per-visitor download detail are the numbers the owner actually wants; page-view trivia is secondary.

This feature fixes the download experience, puts release visibility under an admin-controlled setting, and grows the admin area into a full portal built around downloads and the people who make them.

## Clarifications

### Session 2026-09-12

Three decisions were taken together, because they form one cluster: how identifiable the site's
visitor data is allowed to be. The owner chose the maximum-detail option in each case.

- **Q: At what precision are client addresses stored?** → **A: Full addresses, for the whole retention period.** This reverses the site's existing design, which never persisted a full address (truncated network prefix only). Recorded in FR-038.
- **Q: Must one person stay the same person across days?** → **A: Yes — persistent identity from a first-party cookie.** This replaces the current daily re-salted identifier, under which cross-day identity was impossible by construction. Recorded in FR-022.
- **Q: How long are identifiable per-person records kept?** → **A: 365 days.** Recorded in FR-037.
- **Q: Who sees the consent banner — everyone, or only visitors in consent-required jurisdictions?** → **A: Everyone, regardless of location.** No geo-gating. Recorded in FR-043a. This costs individual-level coverage compared with a geo-gated banner, and that cost is accepted deliberately: it keeps the consent path free of any dependency on the geo database, removes a whole class of "misplaced visitor" failure, and means the site behaves the same way for every visitor.
- **Q: Does the consent request block the page?** → **A: No — a non-blocking bar with explicit Accept and Decline; an explicit dismissal records Decline.** Recorded in FR-043b. Silence is never treated as consent, and the download path is never obstructed.

- **Q: What does the public download page do when the release-visibility setting cannot be read?** → **A: Serve the page using the documented default (latest 3), and make the failure loud but not fatal.** Recorded in FR-016a. The setting is resolved into memory at startup and refreshed on save, so the render path never reads the store; a settings problem can neither break nor slow the primary call to action.

**Combined effect of the first two answers, stated plainly**: asking everyone and never blocking is the most visitor-respecting combination available, and it will produce the **smallest** individuals list of any combination considered. Individual-level figures will therefore cover a minority of traffic, and the portal must present them accordingly (FR-047). This is an accepted, deliberate trade in favour of the download conversion path (US1), not an oversight to be corrected later by making the prompt more insistent.

**Consequence taken into scope rather than assumed away**: a first-party identity cookie is not
strictly necessary for the site to function, and full addresses retained for a year are personal
data held long-term. Together these require an explicit consent mechanism, an accurate published
privacy notice, and a working path for a visitor to refuse, withdraw, and request deletion. Those
are specified in FR-043 through FR-050 and are part of this feature, not follow-on work.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A visitor downloads AKML SQL without waiting (Priority: P1)

Someone lands on the download page from a search result or a link. The page appears immediately with the current release, its size, its hash and one obvious download button. They click it and the file begins transferring right away — no blank period, no spinner, no wondering whether the click registered. They are not asked to scan a wall of sixteen near-identical build numbers to work out which one is current.

**Why this priority**: This is the site's conversion path. Every second of delay and every point of confusion here costs an install, and it is the defect the owner reported first. It stands alone: fixing it delivers full value even if nothing else in this feature ships.

**Independent Test**: Request the download page cold (first request after a restart) and warm, measure time to a usable page in both cases, then click the primary button and measure time until bytes start arriving. Confirm the page shows one clearly marked current release and that the historical list is not what a visitor has to read first.

**Acceptance Scenarios**:

1. **Given** the site has just been restarted or has been idle, **When** a visitor requests the download page, **Then** the page is usable within the cold-start budget in SC-001 and shows the current release without further interaction.
2. **Given** the download page is displayed, **When** the visitor clicks the primary download button, **Then** the transfer visibly begins within the budget in SC-002, and the click is recorded exactly once.
3. **Given** the manifest lists many releases, **When** the page renders, **Then** the current release is visually dominant and older releases are secondary (collapsed, paginated, or hidden per the release-visibility setting) rather than an equally-weighted list.
4. **Given** an advertised installer file is missing from the download folder, **When** the page renders, **Then** that release is not offered as a dead link and the visitor still sees a working way to get the software.
5. **Given** a visitor's connection drops mid-transfer, **When** they retry, **Then** the transfer resumes rather than restarting, and the retry does not count as a second download.

---

### User Story 2 - The owner controls which versions the public sees (Priority: P2)

The owner signs in to the admin portal, opens settings, and chooses how much release history the download page publishes: only the latest release, the latest few, or everything. The change takes effect on the public site without a rebuild, a redeploy, or editing a file on the server. Publishing a new build does not silently reintroduce the clutter, because the setting — not the manifest — decides what is shown.

**Why this priority**: It is the owner's stated request and it directly protects the P1 fix from regressing every time a build is published. It depends on an admin settings surface existing, which is why it follows the download fix rather than leading.

**Independent Test**: Sign in, set release visibility to "latest only", load the public download page in a signed-out browser and confirm only the current release is offered; change the setting to show more and confirm the page follows, all without restarting the site.

**Acceptance Scenarios**:

1. **Given** the owner is signed in to the admin portal, **When** they open the settings area, **Then** they see a release-visibility control with its current value and a plain-language explanation of what each choice publishes.
2. **Given** the owner sets release visibility to "latest only", **When** any visitor loads the download page, **Then** exactly one release is offered and no historical list is rendered.
3. **Given** the owner sets a specific number of releases to show, **When** a new release is published, **Then** the page shows the newest releases up to that number and drops the oldest beyond it, with no further owner action.
4. **Given** the owner changes the setting, **When** the change is saved, **Then** the public page reflects it on the next request, and the previous value is recoverable by setting it back — no data is destroyed.
5. **Given** a release is hidden from the page by the setting, **When** someone uses a direct link to that installer that was published earlier, **Then** the download still works and is still counted — hiding governs what is advertised, not what is reachable.

---

### User Story 3 - The owner sees who downloaded and from where (Priority: P3)

The owner opens the portal and sees downloads as the headline: how many, of which version, from which countries, and in which direction the trend is moving. They can group downloads by country and see the countries ranked. They can open a single visitor and see that person's story — when they first arrived, which pages they touched, whether they downloaded, which file, from what network and device. They can filter the list of people by country, by whether they downloaded, and by date range, and export what they are looking at.

**Why this priority**: This is the reporting value the owner asked for, and it is the reason the portal exists. It is third because it is worthless if the download path itself is broken, and because it needs the portal shell to live in.

**Independent Test**: Generate downloads and visits from several distinct clients, then confirm the portal reports the correct per-country download counts, lists each individual with their own activity, and that filtering by country and by "downloaded" narrows the list correctly.

**Acceptance Scenarios**:

1. **Given** downloads have been recorded, **When** the owner opens the downloads view, **Then** they see downloads grouped by country, ranked by count, with each country's share of the total.
2. **Given** downloads have been recorded across several releases, **When** the owner opens the downloads view, **Then** they see which version each download was of, so uptake of a new release is visible.
3. **Given** a list of individuals, **When** the owner opens one, **Then** they see that individual's first and last seen time, visit count, pages viewed, country, network, device and browser, and every download they made with its timestamp and file.
4. **Given** the owner selects a country, **When** the individual list is filtered, **Then** only people resolved to that country are listed and every summary figure on screen reflects the filter.
5. **Given** the owner selects a date range, **When** any metrics view is displayed, **Then** all figures, charts and lists on that view describe that range, and the range is visible on screen so a screenshot is never ambiguous.
6. **Given** the owner wants the data elsewhere, **When** they export the current view, **Then** they receive a file containing the rows they are looking at, including the active filters in its name or header.
7. **Given** a visitor's country cannot be resolved, **When** they appear in any grouping, **Then** they are counted under an explicit "unknown" bucket rather than dropped, so totals always reconcile.
8. **Given** some visitors refused tracking, **When** the owner views the individuals list, **Then** the portal states what share of traffic in that range is unattributed, so the list is not read as the whole audience.
9. **Given** a consenting visitor returns days later and downloads, **When** the owner opens that individual, **Then** the earlier visit and the later download appear as one person's history, not two.

---

### User Story 4 - The owner runs the whole site from one portal (Priority: P4)

Instead of three loose pages, the owner gets a portal with persistent navigation: an overview, downloads, people, pages, errors, releases and settings. Every section shares the same date range, the same sign-in, and the same look. The owner can see at a glance which installer files are actually present on the server and whether each advertised release is downloadable, and can reach every setting this feature introduces from one place.

**Why this priority**: It is organisational rather than functional — the data and controls in P1–P3 are valuable without it — but it is the difference between "some admin pages" and the "full admin portal management" the owner asked for.

**Independent Test**: Sign in and reach every section from the portal navigation without typing a URL; confirm the date range chosen in one section is still applied when moving to another, and that signing out blocks every section.

**Acceptance Scenarios**:

1. **Given** the owner is signed in, **When** they are anywhere in the portal, **Then** persistent navigation lists every section and indicates which one is active.
2. **Given** the owner sets a date range in one section, **When** they navigate to another section, **Then** that range is still applied.
3. **Given** the owner is signed out or their session has expired, **When** they request any portal section or export, **Then** they are sent to sign-in and no data is disclosed.
4. **Given** the owner opens the releases section, **When** it renders, **Then** each advertised release shows whether its installer is present and downloadable, and its size.
5. **Given** the owner changes any setting, **When** the change is saved, **Then** they get explicit confirmation of what changed and when it takes effect.

---

### User Story 5 - A visitor decides whether to be tracked (Priority: P3, ships with US3)

A first-time visitor is asked, once and clearly, whether they agree to being recognised on return visits. If they agree, they get a persistent identifier and the owner can see their journey. If they decline, the site works exactly as before for them — including the whole download path — they are never assigned an identifier, their address is not stored against a person, and they are not asked again on the next page. Either way they can change their mind later and ask for their data to be deleted.

**Why this priority**: It is not optional decoration — it is the mechanism that makes US3 lawful to operate, so the two ship together. Splitting it out keeps it independently testable and prevents it being quietly dropped as "polish" at the end of US3.

**Independent Test**: Visit as a fresh browser, decline, and confirm: the download works, no identifier is issued, no address is stored against a person, the prompt does not reappear, and the visitor does not appear in the individuals list while still contributing to totals. Then accept in another fresh browser and confirm the inverse. Then withdraw and confirm recording stops.

**Acceptance Scenarios**:

1. **Given** a visitor with no stored choice, **When** they first load any page, **Then** they are asked once, in plain language, what is collected and why, with accept and decline equally available — in a bar that obscures nothing and blocks nothing.
1a. **Given** the consent bar is showing, **When** the visitor ignores it entirely and clicks the download button, **Then** the download proceeds normally and their choice remains unresolved — silence is not consent.
1b. **Given** the consent bar is showing, **When** the visitor dismisses it explicitly, **Then** the dismissal is recorded as Decline and the bar does not return.
2. **Given** a visitor declines, **When** they browse and download, **Then** everything works, no persistent identifier is issued, no address is stored against an individual, and they are counted only in aggregate totals.
3. **Given** a visitor declined earlier, **When** they return, **Then** they are not asked again.
4. **Given** a visitor accepted earlier, **When** they return days later, **Then** they are recognised as the same individual.
5. **Given** a visitor who accepted, **When** they withdraw consent, **Then** per-person recording stops immediately and they are given a route to request deletion of what was already collected.
6. **Given** any visitor, **When** they look for it, **Then** the privacy notice tells them what is stored, for how long, and how to get a copy or have it deleted — and it matches what is actually stored.

---

### Edge Cases

- **Empty state**: no releases published, no visits, no downloads yet — every view explains what is missing rather than rendering blank panels or zeroes with no context.
- **Cold start**: the first request after a deploy or an idle recycle is the one most likely to be a real visitor arriving from a search result; it must meet the cold budget, not a warmed-up one.
- **Manifest disagrees with disk**: a release is advertised but its installer is absent, or an installer is present that no release advertises — both are visible to the owner and neither breaks the public page.
- **Very large history**: a manifest with hundreds of releases must not slow the public page, because the setting bounds what is rendered and what is checked.
- **Geo database absent**: country data is an enrichment; without it every view still works and countries report as unknown.
- **Site behind a proxy or CDN**: if every request appears to come from one address, individual identification collapses; the portal must make that condition visible rather than reporting one visitor for the whole world.
- **Automated traffic**: crawlers and scripted clients must not appear as people in the individuals list; an installer fetched by a script is still a real download and is still counted.
- **Resumed and repeated downloads**: a resumed transfer is not a new download; the same person downloading twice deliberately is two downloads but one person.
- **Concurrent settings edits**: two portal sessions changing the same setting must not corrupt it; the last save wins and is recorded.
- **Settings unreadable**: the download page serves on the documented default and says nothing to the visitor; the owner is told through the logs, the health probe and the portal.
- **Consent bar ignored forever**: a visitor who never interacts is never tracked and is asked again on each visit. That is the accepted cost of not blocking and not inferring consent from silence.
- **Retention boundary**: identifiable data ageing past 365 days disappears from the individuals view without leaving orphaned totals that no longer reconcile; aggregate history for the same period must still add up after the identifiable detail is gone.
- **Deletion request**: an individual's records can be located and removed on request, by identifier or by address.
- **Direct link to a hidden release**: still serves and still counts (US2 scenario 5).
- **Consent refused**: the visitor downloads normally, contributes to totals, and never appears as an individual — and the portal says how many such visitors there were, so the individuals list is not read as complete.
- **Cleared cookies or private browsing**: the same human returns as a new individual. The portal must not present this as audience growth, and must not try to re-link them by address or fingerprint.
- **One human, several devices**: counted as several individuals. The portal must not claim otherwise.
- **Consent given, then withdrawn**: recording stops at withdrawal; already-collected records remain until deleted on request or aged out, and the portal must be able to find them.
- **Shared or carrier-grade addresses**: many people behind one address are distinguished by the persistent identifier, not the address — so address-based grouping and person-based grouping can legitimately disagree, and the portal must not present them as the same number.

## Requirements *(mandatory)*

### Functional Requirements

#### Download experience (US1)

- **FR-001**: The download page MUST present the current release as the primary, visually dominant element, including its version, release date, supported hosts, download size and verification hash.
- **FR-002**: The download page MUST render within the budgets in SC-001 on both a cold and a warm request, irrespective of how many releases the manifest contains.
- **FR-003**: The system MUST NOT repeat per-release availability or size work more than once per release per page render.
- **FR-004**: Clicking the primary download control MUST begin the file transfer within the budget in SC-002, with no intermediate page.
- **FR-005**: The system MUST record each download exactly once, counting neither resumed transfers nor the tracking beacon as additional downloads.
- **FR-006**: The system MUST NOT offer a release whose installer is not actually retrievable.
- **FR-007**: When no release is available, the download page MUST show a friendly explanation and a working alternative route to the software, never an error page.
- **FR-008**: Interrupted downloads MUST be resumable.
- **FR-009**: The download page MUST remain fully usable without client-side scripting, including the primary download action.
- **FR-010**: Release history, when shown, MUST be visually secondary to the current release.

#### Release visibility setting (US2)

- **FR-011**: The portal MUST provide a release-visibility setting with at least: show only the latest release; show the latest N releases; show all releases.
- **FR-012**: When "latest N" is chosen, the owner MUST be able to set N, and the system MUST reject values outside a sane range with a clear message.
- **FR-013**: Changes to the setting MUST take effect on the public site on the next request, without restart, rebuild or redeploy.
- **FR-014**: The setting MUST persist across restarts and deploys.
- **FR-015**: The setting MUST govern only what the download page advertises; a previously published direct download link MUST continue to work and continue to be counted.
- **FR-016**: The system MUST apply a documented default when no setting has been saved, and that default MUST NOT be "show every release".
- **FR-016a**: The download page MUST NOT depend on the settings store being readable at render time. The current value MUST be resolved into memory and refreshed when it changes. If the setting cannot be read at all, the page MUST serve using the documented default rather than failing, and the failure MUST be recorded in the logs, reported by the health probe, and shown in the portal — loud, but never fatal to the public page.
- **FR-017**: The portal MUST show the owner the effect of the current setting (which releases the public sees) before and after a change.

#### Download and visitor metrics (US3)

- **FR-018**: The system MUST record, for each download: time, file, release version, resolved country, network identifier, device, operating system, browser, language, referrer and campaign.
- **FR-019**: The portal MUST report downloads grouped by country, ranked, with counts and share of total, over the selected date range.
- **FR-020**: The portal MUST report downloads grouped by release version.
- **FR-021**: The portal MUST report downloads over time across the selected range.
- **FR-022**: The system MUST maintain a per-individual record spanning that individual's visits and downloads, identified by a persistent first-party identifier issued to the visitor's browser. The identifier MUST remain stable across sessions, days and the full retention period, so a returning visitor is recognised as the same person and a multi-day journey from first visit to download is reportable as one story.
- **FR-022a**: The portal MUST report returning versus new individuals over the selected range, since cross-day identity is what makes that distinction possible.
- **FR-022b**: When a visitor arrives without the identifier (first visit, cleared browser data, private browsing, or consent refused per FR-044), the system MUST NOT attempt to re-link them to an existing individual by any other signal; they are a new individual or, where consent is absent, no individual at all.
- **FR-023**: The portal MUST list individuals with, at minimum: first seen, last seen, country, visit count, download count and whether they downloaded.
- **FR-024**: The portal MUST offer a detail view for one individual showing their visits and downloads in time order.
- **FR-025**: The portal MUST allow the individual list to be filtered by country, by whether the person downloaded, and by date range, with every visible figure honouring the filters.
- **FR-026**: The portal MUST allow the individual list and every metrics view to be exported in a spreadsheet-readable format, carrying the active filters and range.
- **FR-027**: The system MUST exclude automated clients from individual and visitor figures while continuing to count installer downloads made by scripted clients, and MUST report automated traffic separately rather than discarding it.
- **FR-028**: Every grouping MUST include an explicit "unknown" bucket so displayed totals reconcile with headline counts.
- **FR-029**: The system MUST record the full client address (FR-038), and the portal MUST display it to the owner on the individual detail view alongside the derived network and country.
- **FR-030**: Page-visit reporting MUST be retained but MUST NOT occupy the portal's primary position; downloads and people lead.

#### Admin portal (US4)

- **FR-031**: The portal MUST present persistent navigation across all sections: overview, downloads, people, pages, errors, releases, settings.
- **FR-032**: The selected date range MUST persist across sections within a session and MUST be visible on every section.
- **FR-033**: Every portal section, export and settings action MUST require an authenticated session and MUST disclose nothing when unauthenticated.
- **FR-034**: The portal MUST list the installer files actually present on the server alongside the advertised releases, flagging advertised-but-missing and present-but-unadvertised cases.
- **FR-035**: Settings changes MUST be confirmed on screen and recorded with what changed, when, and from which session.
- **FR-036**: The portal MUST be usable on a phone-width screen for the overview and downloads sections at minimum.

#### Privacy, retention and security

- **FR-037**: The system MUST retain identifiable visitor and download records — full address, persistent identifier, and everything linked to them — for **365 days**, and MUST remove or de-identify records past that boundary automatically, without manual intervention. The retention period MUST be configurable by the owner, with 365 days as the shipped default.
- **FR-038**: The system MUST store the **full client address** for each visit and each download, retained for the period in FR-037. This deliberately replaces the current behaviour, under which only a truncated network prefix was ever persisted; the truncated form MAY still be derived for grouping, but the full address is the stored value.
- **FR-039**: The site's published privacy notice MUST state what is collected (including that full addresses and a persistent identifier are stored), why, how long it is kept, the legal basis, and how to refuse, withdraw, request a copy, or request deletion — and MUST match actual behaviour at all times.
- **FR-040**: The owner MUST be able to locate and delete all records for one individual, identified by either their persistent identifier or their address, in a single action that leaves no orphaned rows.
- **FR-041**: Metrics collection MUST never delay, degrade or fail a visitor's page load or download; a metrics failure MUST be invisible to the visitor.
- **FR-042**: Settings that alter public behaviour MUST be changeable only by an authenticated administrator, and MUST be validated before being applied.

#### Consent and data protection (consequence of FR-022, FR-037, FR-038)

- **FR-043**: Because the persistent identifier is not strictly necessary for the site to function, the system MUST obtain the visitor's affirmative consent before issuing it, and MUST NOT issue it — or store a full address against an individual — until consent is given.
- **FR-043a**: The consent request MUST be presented to **every** visitor with no recorded choice, regardless of their country. The system MUST NOT vary the request by geography, and the consent path MUST NOT depend on the geo database being present or the visitor's country being resolvable.
- **FR-043b**: The consent request MUST be non-blocking. It MUST NOT obscure, delay or disable any part of the page — the download control above all — and the visitor MUST be able to complete the entire download path without interacting with it. It MUST offer Accept and Decline with equal prominence, and an explicit dismissal MUST be recorded as Decline. Silence, scrolling, or navigating away MUST NOT be treated as consent, and MUST leave the choice unresolved.
- **FR-044**: When consent is refused or not yet given, the site MUST remain fully functional, including the complete download path. The visitor MUST still be counted in aggregate visit and download totals, MUST NOT be assigned a persistent identifier, and MUST NOT appear in the individuals list.
- **FR-045**: A visitor MUST be able to withdraw consent as easily as they gave it. Withdrawal MUST stop further per-person recording immediately and MUST provide a route to have existing records for that person deleted.
- **FR-046**: The consent choice MUST itself be remembered across visits, and remembering it MUST NOT depend on the identity cookie — a visitor who refused must not be asked on every page.
- **FR-047**: The portal MUST show what share of traffic in the selected range is not attributable to an individual because consent was absent, so the individuals list is never mistaken for the full picture.
- **FR-048**: Stored full addresses MUST be reachable only through an authenticated portal session. They MUST NOT appear in application logs, error reports, client-side responses, the sitemap, or any unauthenticated surface.
- **FR-049**: Exports that contain full addresses or persistent identifiers MUST be produced only for an authenticated administrator and MUST be labelled as containing personal data.
- **FR-050**: The owner MUST be able to produce a copy of everything held about one individual, in a readable format, to satisfy a subject access request.

### Key Entities

- **Release**: a published version of the installer — version, release date, supported hosts, minimum OS, verification hash, notes, where the file lives, and whether it is currently retrievable.
- **Release visibility setting**: the owner's choice of how much release history the public download page advertises; one value, persisted, effective immediately.
- **Visit**: one page view — time, page, referrer, device, browser, operating system, language, campaign, country, network, server handling time.
- **Download**: one installer acquisition — time, file, release version, and the same acquisition context as a visit.
- **Individual**: one consenting browser, tracked by a persistent first-party identifier — identifier, first seen, last seen, full address, country, network, device profile, consent state, their visits and their downloads. One human on two devices is two individuals.
- **Consent record**: a visitor's decision about per-person tracking — the choice, when it was made, and when it was withdrawn; stored independently of the identity cookie so a refusal survives.
- **Country grouping**: an aggregate of visits, individuals and downloads resolved to one country, including an explicit unknown bucket.
- **Site setting**: any owner-controlled value that changes public behaviour — name, current value, allowed values, who changed it and when.
- **Admin session**: an authenticated portal session with its selected date range and filters.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The download page is usable within 3 seconds on the first request after a restart or idle period, and within 1 second on subsequent requests, measured at the 95th percentile.
- **SC-002**: The installer transfer visibly begins within 2 seconds of the visitor clicking the primary download control, at the 95th percentile.
- **SC-003**: Download-page render time does not vary by more than 20% between a manifest holding one release and one holding a hundred.
- **SC-004**: A first-time visitor can identify the current version and start downloading it without scrolling past other versions, on both desktop and phone widths.
- **SC-005**: The owner can change what versions the public sees and verify the change on the live site in under 1 minute, with no deployment.
- **SC-006**: The owner can answer "how many downloads from each country in the last 30 days" in under 15 seconds from opening the portal, without exporting.
- **SC-007**: The owner can locate one individual and see their full visit-and-download history in under 30 seconds.
- **SC-008**: Displayed totals reconcile: per-country, per-version and per-individual figures sum to the headline count for the same range, with unknowns shown explicitly.
- **SC-009**: Recorded downloads match actual successful transfers within 2%, with resumed transfers and tracking beacons causing no double counting.
- **SC-010**: Every portal section is reachable from persistent navigation in one click, and the selected date range survives navigation between all of them.
- **SC-011**: No unauthenticated request to any portal section or export discloses any metric.
- **SC-012**: Metrics collection adds no more than 10 milliseconds to any visitor request, and a metrics outage leaves the public site fully functional.
- **SC-013**: The published privacy notice matches actual collection and retention behaviour, verified by inspection of stored records.
- **SC-014**: A visitor who refuses tracking completes the entire download path with no degradation, appears in no individual-level view, and has no persistent identifier or address stored against them — verified by inspection of stored records.
- **SC-015**: No full address or persistent identifier is retrievable from any unauthenticated surface — pages, exports, application logs, or error reports — verified by search across all of them.
- **SC-016**: For any selected range, the portal states what share of traffic is unattributed because consent was absent, so the individuals list is never presented as complete.
- **SC-017**: The owner can fulfil a deletion request or produce one individual's full record in under 5 minutes.
- **SC-018**: No identifiable record older than the configured retention period exists in storage, verified by inspection after the boundary passes.
- **SC-019**: A returning visitor who consented is recognised as the same individual across days, and new-versus-returning figures reconcile to the individual count for the range.
- **SC-020**: With the settings store made unreadable, the download page still serves the default selection within the SC-001 budgets, and the failure is visible to the owner in the logs, the health probe and the portal.
- **SC-021**: A visitor who never interacts with the consent bar can complete the full download path with no added delay, and remains absent from every individual-level view.

## Assumptions

- **Single administrator**: the portal serves one owner-operator. No multi-user accounts, roles or permissions are in scope; the existing single-password sign-in remains the model.
- **Default release visibility**: if the owner never touches the setting, the page shows the latest release plus a small number of recent ones — not the whole history. The current 16-entry list is treated as the defect, not the baseline.
- **"Show last versions only" means advertised, not deleted**: hidden releases remain on the server and remain downloadable by direct link. Nothing is deleted by a visibility change.
- **Country is the finest location collected**: city and region stay out of scope, matching the existing deliberate choice and the owner's stated need to "group people by country".
- **Page-visit metrics are retained, not removed**: the owner called them "fine but not important", so they stay available but move out of the portal's lead position.
- **The date range applies everywhere**: all metrics views share one range control rather than each inventing its own.
- **Existing history is preserved**: records already collected remain readable in the new portal; older rows simply lack fields that did not exist when they were written.
- **Geo lookup stays offline**: no per-request third-party geo service, so visitor addresses never leave the server. This matters more now that full addresses are stored.
- **A consent mechanism is in scope**: the 2026-09-12 decisions (persistent cookie identity, full addresses, 365-day retention) make one necessary. It is specified in FR-043–FR-050 and is part of this feature, not a follow-on.
- **Identity is per-browser, not per-human**: the persistent identifier tracks a browser. Two devices, or one cleared cookie, means two individuals. Every "person" figure in the portal carries that caveat.
- **Consent-gated tracking reduces coverage**: individual-level figures describe consenting visitors only, and will be lower than aggregate totals. Because the request is shown to every visitor worldwide (FR-043a) rather than only where consent is legally required, that gap is larger than a geo-gated design would produce. It is reported (FR-047), not hidden.
- **Aggregate history may outlive identifiable detail**: after the 365-day boundary the totals for an old period may remain while the per-person detail behind them is gone; views beyond the window report aggregates only.
- **Existing anonymised history is not retrofitted**: records collected before this feature have no persistent identifier and only a truncated address. They stay readable as aggregates and do not resolve to individuals.
- **"Full admin portal management" is bounded to this feature's surface**: navigation, settings, metrics, people, release visibility and installer-file visibility. Editing release metadata, uploading installers through the browser, and content editing are out of scope unless raised later.
- **Slow start covers both cold start and click-to-transfer**: the requirement is written to cover the whole path from request to bytes arriving, since both are within this feature's remit and both are budgeted separately in SC-001/SC-002.

## Dependencies

- An offline country database must be present on the server for country grouping to report anything other than unknown; its absence degrades the feature but does not break it.
- If the site is placed behind a proxy or CDN, the real client address must be made available to the site, or both individual-level reporting and the stored address (FR-038) become meaningless — every visitor collapses to the proxy.
- The installer download folder must remain accessible to the site for availability, size and file-listing reporting.
- A published privacy notice must exist and be kept in step with actual behaviour (FR-039); the consent mechanism cannot ship without it.
- Downloads that redirect to an external mirror must still pass through the site first, or the address and country of those downloads are never observed.

## Out of Scope

- Multi-user administration, roles, or delegated access.
- Editing release metadata or uploading installers through the portal.
- Real-time or live-updating dashboards; date-ranged reporting is sufficient.
- Marketing attribution beyond the campaign parameters already captured.
- Any change to the desktop extension, engine, or web edition.
