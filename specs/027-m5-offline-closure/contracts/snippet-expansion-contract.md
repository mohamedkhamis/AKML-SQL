# Contract: Snippet expansion, surround-with, management & import/export (US1)

**Spec**: [spec.md](../spec.md) · **Research**: [research.md](../research.md) Decision 1 · **FRs**: FR-001 … FR-007

This contract defines the browser-side snippet behaviour. No engine round-trip for expansion; the bridge is used only for the already-shipped best-effort save/delete propagation.

## Placeholder grammar (the body format the browser interprets)

The `WebSnippet.Body` (a `string[]` of lines, joined with `\n` on insert) embeds:

| Token | Meaning | Source |
|---|---|---|
| `${1:label}`, `${2:label}`, … | Numbered tab-stops; `label` is the initial placeholder text. Caret lands on `${1}` first; Tab advances. | CodeMirror `snippet()` native syntax |
| `${name:default}` | Named placeholder bound to a `Variables[]` entry; `default` is the initial text. | engine `Snippet` shape (e.g. `cte` built-in) |
| `${0}` / end | Final caret resting position after the last Tab. | CodeMirror convention |
| `$selected$` | **Surround-with only**: replaced by the user's current selection. | this contract |

**Mapping rule**: before handing the body to CodeMirror's `snippet()`, the browser normalises `${name:default}` named placeholders to numbered tab-stops in `Variables[]` order, so a single CM snippet call drives navigation. A malformed body (unbalanced `${}`) ⇒ literal insertion of the raw body, no throw (edge case).

## Expansion trigger (FR-002)

1. User types a shortcode (e.g. `ssf`) in the editor; the completion source surfaces matching snippets as completion items **visually distinct** from schema/keyword items (FR-001 edge case: a snippet item type so an accidental expand is unlikely).
2. Accepting a snippet completion item invokes a JS `expandSnippet(hostId, snippetBody)` that dispatches a CodeMirror `snippet()` transaction at the caret, replacing the typed shortcode token.
3. Caret lands on the first tab-stop; Tab/Shift-Tab navigate; Escape / edit outside the field ends the session.

**Contract surface added to `akml-editor.js`**: `export function expandSnippet(hostElementId, body)` and `export function surroundSelection(hostElementId, body)`.

## Surround-with (FR-003)

1. Keyboard chord (proposed `Ctrl+K, Ctrl+S` — settled in tasks; must not collide with the existing `Ctrl+K, Ctrl+F`/`Ctrl+K, Ctrl+L` chords on `Editor.razor`) opens a picker filtered to `SurroundsWith == true` snippets.
2. On choose: `surroundSelection` wraps the current selection — the body's `$selected$` token is replaced by the selection text, remaining tab-stops behave as normal expansion.
3. No selection ⇒ defined behaviour: insert at caret with `$selected$` empty (no crash) — edge case.

## Management surface (FR-004)

A new page (proposed route `/snippets`, linked from `NavMenu.razor` alongside Schema cache) renders:

- **List**: built-ins first (read-only, badge), then personal snippets, by title.
- **Create / Edit**: shortcode, title, description, body (multiline), variables; persisted via `ISnippetStore.SaveAsync`.
- **Delete**: personal only; built-ins refuse with a clear message (FR-004; already enforced by `ISnippetStore`).
- Changes survive reload (IndexedDB `snippets` store).

## Import / export (FR-005, FR-006)

- **Import**: `<InputFile accept=".akmlsnippet">` → read text → `JsonSerializer.Deserialize<WebSnippet>` → validate (shortcode present; not a `builtin.*` id) → `SaveAsync`. Malformed JSON ⇒ rejected with a status message, existing library intact (no partial write). Shortcode collision with a built-in ⇒ rejected or renamed; never overwrites a `builtin.*`.
- **Export**: serialise the selected personal snippet to `.akmlsnippet` JSON and trigger a download via the existing `wwwroot/js/akml-download.js` (`downloadBase64`), filename `<shortcode>.akmlsnippet` with reserved-char sanitisation.
- **Round-trip (FR-006, SC-002)**: export → re-import yields a byte-identical `WebSnippet`; the JSON uses the shared field names (`metadata`/`variables`/`body`, camelCase) so the engine `SnippetLoader.LoadSingle` and the WPF surface load it without warnings.

## Test contract

bUnit + JS-interop-mocked tests under `tests/AkmlSql.Web.Tests/Snippets/`:

- expansion inserts body + positions caret at tab-stop (interop call asserted);
- surround wraps selection; no-selection no-crash;
- management create/edit/delete persists; built-in delete refused;
- import happy path + malformed-rejected + builtin-collision-rejected;
- export → re-import round-trip byte-identical (FR-006).
