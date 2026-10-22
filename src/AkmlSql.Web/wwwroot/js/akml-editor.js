// Spec 021 (web edition) -- M2 task T032. CodeMirror 6 wrapper exposing a Blazor-facing
// API. The Blazor EditorComponent.razor calls into this module via IJSRuntime; it does
// not know that the underlying editor is CM6 -- it could be swapped for Monaco or any
// other editor by replacing this file.
//
// CodeMirror 6 is loaded from a locally-vendored bundle (wwwroot/lib/codemirror/akml-cm.js),
// NOT a CDN. This keeps the web edition working on-prem / offline / behind a strict CSP — the
// esm.sh CDN was unreliable (500s / firewall blocks) and required loosening script-src.
// The bundle is produced by src/AkmlSql.Web/tools/codemirror (npm install && npm run build); it
// re-exports each package as a namespace matching the shape destructured below. It MUST stay one
// bundle so @codemirror/state is a single shared instance (per-package files would break facets).
// Resolved relative to this module's URL (/js/akml-editor.js -> /lib/codemirror/akml-cm.js) so it
// is independent of the app's <base href>.
const CM_BUNDLE_URL = new URL('../lib/codemirror/akml-cm.js', import.meta.url).href;
let _cmModulesPromise = null;

function loadCm() {
    if (_cmModulesPromise) return _cmModulesPromise;
    _cmModulesPromise = import(CM_BUNDLE_URL).then(m => ({
        state: m.state,
        view: m.view,
        commands: m.commands,
        language: m.language,
        langSql: m.langSql,
        autocomplete: m.autocomplete,
        search: m.search,
        lint: m.lint,
        highlight: m.highlight,   // `tags` for the token-driven syntax theme
    }));
    return _cmModulesPromise;
}

// One editor instance per hosting element. Keyed by hostElementId.
const _instances = new Map();

/**
 * Spec 041 (R65) — pure: the 1-based line and column of offset `head` in a CodeMirror Text, as the status bar
 * shows them. The column counts characters (a tab is one), as SSMS's "Col" does.
 */
export function caretPosition(doc, head) {
    const at = Math.max(0, Math.min(head, doc.length));
    const line = doc.lineAt(at);
    return { line: line.number, column: at - line.from + 1 };
}

// ── Spec 041 (research R53, FR-053) — editor options ─────────────────────────────────────────────────────

// Spec 043 (research R12, FR-017): the fonts Settings › Editor offers, by the names EditorSettings.FontName gives.
// 'default' is no override: the theme's --akml-type-editor-family (Consolas first) applies.
const EDITOR_FONT_STACKS = Object.freeze({
    'default': null,
    'cascadia-mono': "'Cascadia Mono', 'Cascadia Code', Consolas, monospace",
    'consolas': "Consolas, 'Cascadia Mono', monospace",
    'system-monospace': 'ui-monospace, monospace',
});

/** The CSS font stack of an offered font, or null for the default (and for anything not offered). */
export function editorFontStack(name) {
    return Object.prototype.hasOwnProperty.call(EDITOR_FONT_STACKS, name) ? EDITOR_FONT_STACKS[name] : null;
}

/** The editor options within their limits (Settings › Editor): font size 9–32 px, tab size 1–8, an offered font. */
export function normaliseEditorOptions(opts) {
    const o = opts || {};
    const int = (v, d) => (typeof v === 'number' && Number.isFinite(v) ? Math.round(v) : d);
    return {
        fontSize: Math.min(32, Math.max(9, int(o.fontSize, 13))),
        wordWrap: !!o.wordWrap,
        tabSize: Math.min(8, Math.max(1, int(o.tabSize, 4))),
        fontFamily: typeof o.fontFamily === 'string' && Object.prototype.hasOwnProperty.call(EDITOR_FONT_STACKS, o.fontFamily)
            ? o.fontFamily : 'default',
    };
}

/**
 * Spec 041 (R53) / 043 (R12): the size and the font are CSS only — two variables every SQL surface reads
 * (--akml-editor-font-size, --akml-editor-font-family). Set on the editor host by the editor, and on the document root
 * (applyEditorAppearanceToRoot) so pages without an editor (History, Format styles) follow the same setting.
 */
export function applyEditorAppearance(el, opts) {
    if (!el || !el.style) return;
    const o = normaliseEditorOptions(opts);
    el.style.setProperty('--akml-editor-font-size', `${o.fontSize}px`);
    const stack = editorFontStack(o.fontFamily);
    if (stack) el.style.setProperty('--akml-editor-font-family', stack);
    else el.style.removeProperty('--akml-editor-font-family');
}

/** The appearance on the document root (called by MainLayout on start and when Settings › Editor changes). */
export function applyEditorAppearanceToRoot(opts) {
    applyEditorAppearance(typeof document !== 'undefined' ? document.documentElement : null, opts);
}

// ── Spec 043 (research R14, FR-036) — writing assistants stay out of the SQL ─────────────────────────────
// CodeMirror already sets spellcheck="false" autocorrect="off" autocapitalize="off" on .cm-content. Browser
// writing assistants read their own opt-outs: Grammarly has used data-gramm, data-gramm_editor and
// data-enable-grammarly over the years, LanguageTool reads data-lt-active. An assistant that ignores all of them
// is outside the page's control (Settings › Editor says so).

/** The attributes that ask writing assistants to leave an element alone. */
export const WRITING_ASSISTANT_OPT_OUT = Object.freeze({
    'data-gramm': 'false',
    'data-gramm_editor': 'false',
    'data-enable-grammarly': 'false',
    'data-lt-active': 'false',
});

/** The CodeMirror extension that puts WRITING_ASSISTANT_OPT_OUT on the editor content (pure; `cm` is the bundle). */
export function writingAssistantOptOut(cm) {
    return cm.view.EditorView.contentAttributes.of({ ...WRITING_ASSISTANT_OPT_OUT });
}

/** The CodeMirror extensions for the options, one per compartment (pure; `cm` is the bundle). */
export function optionExtensions(cm, opts) {
    const o = normaliseEditorOptions(opts);
    return {
        wrap: o.wordWrap ? cm.view.EditorView.lineWrapping : [],
        tab: [cm.state.EditorState.tabSize.of(o.tabSize), cm.language.indentUnit.of(' '.repeat(o.tabSize))],
    };
}

/**
 * Spec 042 (FR-060, FR-065, research R23) - the SQL language extension for a database kind (pure; `cm` is the
 * bundle). 'postgresql' gets the PostgreSQL dialect (its keywords, dollar-quoted strings, `::` casts, $n parameters,
 * double-quoted identifiers); anything else, including no connection, is exactly what the editor used before the
 * feature, so SQL Server highlighting does not change.
 */
export function dialectExtension(cm, kind) {
    return isPostgresKind(kind)
        ? cm.langSql.sql({ dialect: cm.langSql.PostgreSQL })
        : cm.langSql.sql();
}

/** Spec 042: true for the PostgreSQL database kind. */
export function isPostgresKind(kind) {
    return typeof kind === 'string' && kind.trim().toLowerCase() === 'postgresql';
}

/**
 * Spec 042 (FR-061, T108) - the word right before the caret, which a completion replaces: `{ from, text }` with `from` an
 * offset into `textBeforeCaret`, or null when the caret is not in a word.
 *
 * SQL Server words may start with '@' or '#'. PostgreSQL has neither: a word is letters, digits, '_' and '$', or a
 * double-quoted name being typed. The opening quote belongs to the word, so accepting "Audit Log" over `"Aud` does not
 * double the quote. A quote is an opening one when an odd number of quotes lie before the caret (a doubled quote inside a
 * name counts as two), so the closing quote of `"Audit Log" ` is not mistaken for the start of a word.
 */
export function wordBefore(kind, textBeforeCaret) {
    const text = typeof textBeforeCaret === 'string' ? textBeforeCaret : '';
    if (!isPostgresKind(kind)) {
        const m = /[@#\w]+$/.exec(text);
        return m ? { from: m.index, text: m[0] } : null;
    }

    let quotes = 0;
    for (const c of text) if (c === '"') quotes++;
    if (quotes % 2 === 1) {
        const at = text.lastIndexOf('"');
        return { from: at, text: text.slice(at) };
    }
    const m = /[\w$]+$/.exec(text);
    return m ? { from: m.index, text: m[0] } : null;
}

/**
 * Spec 042 (FR-053, T107) - the link in a quick-info "Documentation" row. Only the official PostgreSQL manual is linked, and only
 * its https pages: the text comes from the engine, and a page is never handed an arbitrary address to open.
 */
export function documentationLink(detail) {
    if (!detail || detail.label !== 'Documentation' || typeof detail.value !== 'string') return null;
    return /^https:\/\/www\.postgresql\.org\/docs\/\d+\/[a-z0-9\-]+\.html$/.test(detail.value) ? detail.value : null;
}

/**
 * Spec 042 (T108) - the span a completion replaces when the word being typed began with a double quote. The editor closes
 * an opened quote on its own, so `"Aud` is really `"Aud|"`; accepting "Audit Log" over it must take that closing quote too, or
 * the result is `"Audit Log""`. Returns the span unchanged unless the replaced text opens with a quote, the insert is a quoted
 * name, and a quote follows the caret.
 */
export function quotedReplaceSpan(docText, from, to, insertText) {
    const text = typeof docText === 'string' ? docText : '';
    if (typeof insertText !== 'string' || !insertText.startsWith('"')) return { from, to };
    if (text.charAt(from) !== '"') return { from, to };
    if (text.charAt(to) === '"') return { from, to: to + 1 };
    return { from, to };
}

/** The pattern CodeMirror uses to keep a popup open while the typed word still matches. */
export function completionValidFor(kind) {
    return isPostgresKind(kind) ? /^(?:"[^"]*|[\w$]*)$/ : /^[\w]*$/;
}

const POST_KEYWORD_SQLSERVER = /\b(?:where|and|or|from|join|on|set|having|select|group\s+by|order\s+by|by|when|then|else|update|insert(?:\s+into)?|into|delete(?:\s+from)?|exec(?:ute)?)\s+$/i;

// PostgreSQL: its own words, and '::' (a cast takes a type).
const POST_KEYWORD_POSTGRES = /(?:\b(?:where|and|or|from|join|on|set|having|select|group\s+by|order\s+by|by|when|then|else|update|insert(?:\s+into)?|into|delete(?:\s+from)?|returning|using|values|limit|offset|as|conflict|do|union(?:\s+all)?|except|intersect|references|table|index|call|truncate|distinct|lateral|only|case|not|exists)\s+|::\s*)$/i;

/** What, right before the caret, opens the popup without a typed word (after a clause word and a space, or after '::'). */
export function postKeywordTrigger(kind) {
    return isPostgresKind(kind) ? POST_KEYWORD_POSTGRES : POST_KEYWORD_SQLSERVER;
}

/** True when the text before the caret ends a statement (`;`), where no completion list belongs. */
export function endsStatement(lineUpToCaret) {
    return typeof lineUpToCaret === 'string' && lineUpToCaret.endsWith(';');
}

/** The characters that, typed last, may open the popup (a space, a dot, a comma ...); PostgreSQL adds ':' for '::'. */
export function typedTriggerCharacters(kind) {
    return isPostgresKind(kind) ? /[\s.,()=<>!+\-*/:]/ : /[\s.,()=<>!+\-*/]/;
}

// ── Spec 030 — expand "SELECT *" on Tab (SQL Prompt parity) ──────────────────────────────────────────────

// The syntax nodes a '*' inside of which is never a select-list wildcard. Brackets is T-SQL's [quoted name] (the editor's
// SQL Server language parses it as brackets) and PostgreSQL's array subscript.
const NOT_A_WILDCARD_NODES = new Set(['LineComment', 'BlockComment', 'String', 'QuotedIdentifier', 'Brackets']);

// Spec 043 follow-up (review): how long a Tab may wait for the syntax tree to reach the star (right after a large paste or
// a tab switch the editor has not parsed that far yet). Past it, the star is read from the text alone (selectListStar), from
// the start of the document — at most WILDCARD_TEXT_LIMIT characters of it (about 30 ms); a star further in is left alone
// until the tree gets there.
const WILDCARD_TREE_WAIT_MS = 50;
const WILDCARD_TEXT_LIMIT = 1_000_000;
// With the tree, the text is read from the star's own SELECT, looked for among at most this many nodes before the star.
const WILDCARD_SELECT_SEARCH = 20_000;
// How far past the star the text is read for the word that follows it.
const WILDCARD_LOOKAHEAD = 4096;

// Spec 043 follow-up (review): the words that may follow a select-list star (besides the end, ',', ';', ')' and a GO line).
// GO is not among them: only a GO line ends a batch (isBatchSeparator); anywhere else 'go' is a name ('SELECT go, *').
const AFTER_SELECT_LIST_STAR = new Set(['FROM', 'INTO', 'UNION', 'EXCEPT', 'INTERSECT', 'ORDER', 'GROUP', 'WHERE', 'HAVING',
    'OPTION', 'FOR', 'WINDOW', 'LIMIT', 'OFFSET', 'FETCH']);

// Spec 043 follow-up (review): the words that close a SELECT's select list at its own parenthesis level — the clauses after
// it and the statements that may follow it without a ';'. END, ELSE and WITH are not among them: CASE ... END and
// TOP n WITH TIES sit inside a select list. RETURNING closes one on SQL Server only: on PostgreSQL it opens its own list
// (INSERT / UPDATE / DELETE ... RETURNING *), which PgWildcardExpander expands to the target's columns.
const SELECT_LIST_CLOSERS = new Set([...AFTER_SELECT_LIST_STAR, 'RETURNING', 'VALUES', 'SET', 'UPDATE', 'INSERT', 'DELETE',
    'MERGE', 'DECLARE', 'EXEC', 'EXECUTE', 'IF', 'WHILE', 'BEGIN', 'RETURN', 'PRINT', 'USE', 'CREATE', 'ALTER', 'DROP', 'TRUNCATE']);

// The longest word either set holds (INTERSECT, RETURNING): longer words are never upper-cased (selectListStar reads
// whole documents when the syntax tree is not ready).
const LONGEST_LIST_WORD = 9;

// PostgreSQL's $tag$: an unquoted identifier without '$' (a letter or '_', then letters, digits and '_'), or nothing.
const DOLLAR_QUOTE_TAG = /\$(?:[\p{L}_][\p{L}\p{N}_]*)?\$/uy;

function isSqlSpace(c) {
    return c === 32 || (c >= 9 && c <= 13) || c === 0xa0 || c === 0x85 || c === 0x1680 || (c >= 0x2000 && c <= 0x200a)
        || c === 0x2028 || c === 0x2029 || c === 0x202f || c === 0x205f || c === 0x3000 || c === 0xfeff;
}

/** A letter, '_', '@' (SQL Server variables) or '#' (temporary tables); any other non-space character past ASCII. */
function isSqlWordStart(c) {
    return (c >= 65 && c <= 90) || (c >= 97 && c <= 122) || c === 95 || c === 64 || c === 35 || (c > 127 && !isSqlSpace(c));
}

function isSqlWordPart(c) {
    return isSqlWordStart(c) || (c >= 48 && c <= 57) || c === 36;
}

/** The offset just past the quoted text opening at `i`: `close` doubled is an escape, and so is a backslash in E'...'. */
function skipQuoted(text, i, close, backslash) {
    const n = text.length;
    for (i++; i < n; i++) {
        const c = text.charCodeAt(i);
        if (backslash && c === 92) { i++; continue; }
        if (c !== close) continue;
        if (i + 1 < n && text.charCodeAt(i + 1) === close) { i++; continue; }
        return i + 1;
    }
    return n;
}

/**
 * Spec 043 follow-up (review) — the significant tokens of `text` from offset `from` (a token boundary), read the way SQL
 * Server and PostgreSQL both read them: whitespace, '--' comments and (nested) block comments are skipped; a string ('...',
 * N'...', E'...', $tag$...$tag$) or a quoted name ([...], "...") is one token, so a '*' inside one is never a token of its own.
 * Calls `visit(type, start, end)` for each — 'word', 'number', 'string', 'name' or 'punct' (one character) — until it
 * returns false or the text ends.
 */
function scanSqlTokens(text, from, visit) {
    const n = text.length;
    let i = from;
    while (i < n) {
        const c = text.charCodeAt(i);
        if (isSqlSpace(c)) { i++; continue; }
        const d = i + 1 < n ? text.charCodeAt(i + 1) : 0;
        if (c === 45 && d === 45) {                                    // -- to the end of the line
            const eol = text.indexOf('\n', i + 2);
            i = eol < 0 ? n : eol + 1;
            continue;
        }
        if (c === 47 && d === 42) {                                    // /* ... */, nested as both servers nest them
            let depth = 1;
            for (i += 2; i < n && depth > 0;) {
                const a = text.charCodeAt(i), b = i + 1 < n ? text.charCodeAt(i + 1) : 0;
                if (a === 47 && b === 42) { depth++; i += 2; }
                else if (a === 42 && b === 47) { depth--; i += 2; }
                else i++;
            }
            continue;
        }
        const start = i;
        let type = 'punct';
        let tag = null;
        if (c === 39) { i = skipQuoted(text, i, 39, false); type = 'string'; }
        else if (c === 34) { i = skipQuoted(text, i, 34, false); type = 'name'; }
        else if (c === 91) { i = skipQuoted(text, i, 93, false); type = 'name'; }   // [name], ']]' inside it
        else if (c === 36 && (DOLLAR_QUOTE_TAG.lastIndex = i, tag = DOLLAR_QUOTE_TAG.exec(text))) {
            const end = text.indexOf(tag[0], i + tag[0].length);
            i = end < 0 ? n : end + tag[0].length;
            type = 'string';
        } else if (isSqlWordStart(c)) {
            for (i++; i < n && isSqlWordPart(text.charCodeAt(i));) i++;
            const prefix = i - start === 1 ? text.charCodeAt(start) | 32 : 0;   // N'...', E'...', B'...', X'...'
            if (i < n && text.charCodeAt(i) === 39 && (prefix === 110 || prefix === 101 || prefix === 98 || prefix === 120)) {
                i = skipQuoted(text, i, 39, prefix === 101);
                type = 'string';
            } else type = 'word';
        } else if ((c >= 48 && c <= 57) || (c === 46 && d >= 48 && d <= 57)) {
            for (i++; i < n; i++) {
                const e = text.charCodeAt(i);
                if (isSqlWordPart(e) || e === 46) continue;
                if ((e === 43 || e === 45) && (text.charCodeAt(i - 1) | 32) === 101) continue;   // 1e-3
                break;
            }
            type = 'number';
        } else i++;
        if (visit(type, start, i) === false) return;
    }
}

/** Whether `text` breaks a line between offsets `from` and `to`. */
function hasLineBreak(text, from, to) {
    for (let i = from; i < to; i++) {
        const c = text.charCodeAt(i);
        if (c === 10 || c === 13) return true;
    }
    return false;
}

/**
 * Spec 043 follow-up (review) — whether the word GO at [`from`, `to`) of `text` is the batch separator, as SSMS reads it: the
 * first token on its line (`prevEnd`, the end of the token before it, is on an earlier line; -1 when there is none) and
 * followed on its line by nothing but one repeat count and comments ('GO', 'GO 3 -- again'). Anywhere else it is a name:
 * 'SELECT go, *', 'GO, *' or 'GO x' does not end the batch (TsqlParserService.SplitBatches refuses a script with the last).
 */
function isBatchSeparator(text, prevEnd, from, to) {
    if (prevEnd >= 0 && !hasLineBreak(text, prevEnd, from)) return false;
    let separator = true;
    let last = to;
    let count = false;
    scanSqlTokens(text, to, (type, start, end) => {
        if (hasLineBreak(text, last, start)) return false;           // the GO line ended (a '--' comment ends it too)
        if (type === 'number' && !count && /^\d+$/.test(text.slice(start, end))) { count = true; last = end; return true; }
        separator = false;
        return false;
    });
    return separator;
}

/**
 * Spec 043 follow-up (review) — pure: whether the '*' at offset `star` of `sql` starts a select-list item, reading `sql` from
 * its start (which must be a token boundary: the start of the document or of the star's statement). SQL Server's engine
 * takes the FROM tables' columns for whatever star it is asked about, so a product or COUNT's argument must never get here.
 *
 * - The star is a token: not in a comment, a string or a quoted name.
 * - A SELECT's select list is open at the star's parenthesis level: a SELECT came before it at that level, in this
 *   statement (';' and a GO line end one — isBatchSeparator; a 'go' anywhere else is a name), and no clause or statement
 *   word closed it since ('COALESCE(a, *)' and 'COUNT(*)' are a level that holds no SELECT). When `postgres` is set, a
 *   RETURNING list counts too ('DELETE FROM t RETURNING *'); on SQL Server RETURNING is just a word that closes the list.
 * - What comes right before the item — the star, or 'name.' / '[name].' / '"name".' qualifiers written against it — is
 *   SELECT, DISTINCT, ALL, ',', the end of TOP n / TOP (n) [PERCENT] [WITH TIES], or PostgreSQL's DISTINCT ON (...) and
 *   RETURNING.
 * - What comes after the star is the end, ',', ';', ')', a GO line or a clause word (FROM, INTO, UNION, ORDER, WHERE ...).
 *   When `continues` says `sql` is cut short, a star with nothing after it in `sql` is not taken.
 */
export function selectListStar(sql, star, continues = false, postgres = false) {
    const text = typeof sql === 'string' ? sql : '';
    if (!(star >= 0 && star < text.length) || text.charCodeAt(star) !== 42) return false;
    const frame = (before1, before2) => ({ listOpen: false, before1, before2 });
    let frames = [frame(null, null)];
    let recent = [];                 // the last significant tokens: { type, word, punct, variable, from, to, opener }
    let starAt = -1;                 // the star's index in `recent`, once read
    let listOpen = false;            // the select list is open at the star's level
    let after = null;                // the token after the star
    let swallowed = false;           // the star is inside a comment, a string or a quoted name
    let prevEnd = -1;                // the end of the token before this one (-1: none yet)
    scanSqlTokens(text, 0, (type, from, to) => {
        const gap = prevEnd;
        prevEnd = to;
        if (starAt >= 0) { after = { type, from, to, gap }; return false; }
        if (from > star || (from < star && to > star)) { swallowed = true; return false; }
        const word = type === 'word' && to - from <= LONGEST_LIST_WORD ? text.slice(from, to).toUpperCase() : null;
        const punct = type === 'punct' ? text[from] : null;
        // Spec 043 follow-up (review): only a GO line ends the batch; 'SELECT go, *' is a column named go.
        if (punct === ';' || (word === 'GO' && isBatchSeparator(text, gap, from, to))) {
            frames = [frame(null, null)];
            recent = [];
            return true;
        }
        const token = { type, word, punct, variable: type === 'word' && text.charCodeAt(from) === 64, from, to, opener: null };
        const level = frames[frames.length - 1];
        if (punct === '(') {
            const r = recent.length;
            frames.push(frame(r > 0 ? recent[r - 1].word : null, r > 1 ? recent[r - 2].word : null));
        } else if (punct === ')') {
            if (frames.length > 1) token.opener = frames.pop();
        } else if (word === 'SELECT' || (postgres && word === 'RETURNING')) level.listOpen = true;
        else if (word !== null && SELECT_LIST_CLOSERS.has(word)) level.listOpen = false;
        if (from === star) { starAt = recent.length; listOpen = level.listOpen; }
        recent.push(token);
        if (starAt < 0 && recent.length > 64) recent = recent.slice(-32);
        return true;
    });
    if (swallowed || starAt < 0 || !listOpen) return false;

    if (after) {
        const a = text.slice(after.from, after.to);
        const word = after.type === 'word' ? a.toUpperCase() : null;
        if (after.type === 'punct' ? !(a === ',' || a === ';' || a === ')')
            : !(word !== null && (AFTER_SELECT_LIST_STAR.has(word)
                || (word === 'GO' && isBatchSeparator(text, after.gap, after.from, after.to))))) return false;
    } else if (continues) return false;

    // The item starts at the star or at the qualifiers written against it ('t.*', '[dbo].[t].*', '"t".*').
    let p = starAt - 1;
    while (p >= 1 && recent[p].punct === '.' && recent[p].to === recent[p + 1].from
        && (recent[p - 1].type === 'word' || recent[p - 1].type === 'name') && recent[p - 1].to === recent[p].from) p -= 2;
    const before = p >= 0 ? recent[p] : null;
    if (!before) return false;
    if (before.word === 'SELECT' || before.word === 'DISTINCT' || before.word === 'ALL' || before.punct === ',') return true;
    if (postgres && before.word === 'RETURNING') return true;
    if (before.punct === ')' && before.opener && before.opener.before1 === 'ON' && before.opener.before2 === 'DISTINCT') return true;
    // TOP n / TOP @n / TOP (expression), then PERCENT, then WITH TIES.
    let q = p;
    if (recent[q].word === 'TIES' && q >= 1 && recent[q - 1].word === 'WITH') q -= 2;
    if (q >= 0 && recent[q].word === 'PERCENT') q -= 1;
    if (q < 0) return false;
    const last = recent[q];
    if (last.punct === ')') return !!last.opener && last.opener.before1 === 'TOP';
    return (last.type === 'number' || last.variable) && q >= 1 && recent[q - 1].word === 'TOP';
}

/**
 * Spec 030 / 043 — the wildcard Tab expands (pure; `cm` is the bundle): `{ caretOffset }`, the offset just after the '*', when
 * the bare caret sits right after a '*' or — as SQL Prompt in SSMS also takes it — right before one, and that star starts a
 * select-list item; else null, and Tab does what it always does.
 *
 * Spec 043 follow-up (review): Tab is consumed as soon as this says yes, and on SQL Server nothing downstream checks what the
 * star is — the engine takes the FROM tables' columns for any star — so 'price * qty', 'COUNT(*)' or '2 * 3' would have been
 * replaced by a column list. It now says yes only to a star that starts a select-list item (selectListStar), on either side
 * of the caret. The syntax tree still rules out a star in a comment, a string or a quoted name; it is parsed up to the star
 * first (for up to WILDCARD_TREE_WAIT_MS), since right after a large paste or a tab switch it may not reach it yet. With
 * the tree, the text is read from the last SELECT before the star at its own level (none: not a select-list star); without
 * it, from the start of the document, so the comment and string checks hold then too.
 *
 * Spec 043 follow-up (review): `kind` is the editor's database kind (currentKind). On PostgreSQL a RETURNING list is a
 * select list as well ('INSERT ... RETURNING *', 'UPDATE ... RETURNING *', 'DELETE ... RETURNING *'), which the engine's
 * PgWildcardExpander expands to the target's columns; the SELECT gate and selectListStar had shut it out.
 */
export function wildcardAtCaret(cm, state, kind) {
    const postgres = isPostgresKind(kind);
    const sel = state.selection.main;
    if (!sel.empty) return null;                                  // bare caret only, not a range
    const doc = state.doc;
    const at = sel.head;
    let star;
    if (at > 0 && doc.sliceString(at - 1, at) === '*') star = at - 1;
    else if (at < doc.length && doc.sliceString(at, at + 1) === '*') star = at;
    else return null;
    const pos = star + 1;   // the engine is always asked with the caret after the '*'

    let from = 0;           // where the text is read from
    let tree = null;
    try {
        const lang = cm.language;
        tree = (typeof lang.ensureSyntaxTree === 'function' && lang.ensureSyntaxTree(state, pos, WILDCARD_TREE_WAIT_MS))
            || lang.syntaxTree(state);
    } catch { tree = null; /* no tree -> the text alone decides */ }
    if (tree && tree.length >= pos) {
        // Resolved just after the star, so the node is the one holding the star (at the star's start it is the statement).
        const starNode = tree.resolveInner(pos, -1);
        for (let node = starNode; node; node = node.parent) {
            if (NOT_A_WILDCARD_NODES.has(node.name)) return null;
        }
        from = starNode.from === star ? selectBefore(doc, starNode, postgres) : 0;
        if (from < 0) return null;
    }
    if (star - from > WILDCARD_TEXT_LIMIT) return null;

    const to = Math.min(doc.length, pos + WILDCARD_LOOKAHEAD);
    if (!selectListStar(doc.sliceString(from, to), star - from, to < doc.length, postgres)) return null;
    return { caretOffset: pos };
}

// Spec 043 follow-up (review): how soon after the key a declined expansion may still do what Tab does.
export const WILDCARD_TAB_FALLBACK_MS = 250;

const nowMs = () => (typeof performance !== 'undefined' && typeof performance.now === 'function' ? performance.now() : Date.now());

/**
 * Spec 043 follow-up (review) — pure: whether Tab may still do what it would have done (indent) after the engine declined to
 * expand the star — it is not connected, PostgreSQL refused, or the schema is not loaded yet; else Tab, consumed when it was
 * pressed, did nothing. `atKey` and `now` are `{ time, tab, doc, selection }` at the keypress and at the decline: only a
 * decline within WILDCARD_TAB_FALLBACK_MS, with the same query tab shown and the document and selection exactly as they
 * were (the same Text, an equal selection). A later one does nothing: an indent then would surprise.
 */
export function declinedTabMayIndent(atKey, now) {
    if (!atKey || !now) return false;
    if (!(now.time - atKey.time <= WILDCARD_TAB_FALLBACK_MS)) return false;
    if (now.tab !== atKey.tab || now.doc !== atKey.doc) return false;
    return !!now.selection && !!atKey.selection && now.selection.eq(atKey.selection);
}

/**
 * The start of the last SELECT before the star's node among its siblings — the star's own parenthesis level in the SQL
 * language's tree (a statement or a '(...)' group); -1 when there is none within WILDCARD_SELECT_SEARCH nodes. With
 * `postgres`, the last SELECT or RETURNING (spec 043 follow-up, review: 'INSERT INTO t VALUES (1) RETURNING *' has no SELECT).
 */
function selectBefore(doc, starNode, postgres = false) {
    const cursor = starNode.cursor();
    for (let seen = 0; seen < WILDCARD_SELECT_SEARCH && cursor.prevSibling(); seen++) {
        const length = cursor.to - cursor.from;
        if (length !== 6 && !(postgres && length === 9)) continue;
        const word = doc.sliceString(cursor.from, cursor.to).toUpperCase();
        if (word === 'SELECT' || (postgres && word === 'RETURNING')) return cursor.from;
    }
    return -1;
}


/**
 * Create a CodeMirror editor inside the given host element.
 * @param {string} hostElementId  The DOM id of the container div.
 * @param {string} initialText    Initial document text.
 * @param {object} dotNetRef      Blazor DotNetObjectReference for OnTextChanged callback.
 * @param {{fontSize?:number, wordWrap?:boolean, tabSize?:number}} [options]  Spec 041 — the editor options.
 * @returns {Promise<void>}
 */
export async function create(hostElementId, initialText, dotNetRef, options) {
    const host = document.getElementById(hostElementId);
    if (!host) {
        throw new Error(`akml-editor: host element '${hostElementId}' not found.`);
    }
    if (_instances.has(hostElementId)) {
        // Already created -- replace the content.
        await setText(hostElementId, initialText ?? '');
        return;
    }

    const cm = await loadCm();

    // Spec 030 web redesign — token-driven SQL syntax theme. Colours resolve from the --akml-syntax-*
    // CSS custom properties (generated from docs/theme-tokens.json), so the editor follows the active
    // light/dark/HC theme automatically instead of CM6's hardcoded defaultHighlightStyle.
    const t = cm.highlight.tags;
    const akmlHighlightStyle = cm.language.HighlightStyle.define([
        { tag: t.keyword, color: 'var(--akml-syntax-keyword)' },
        { tag: [t.string, t.special(t.string), t.character], color: 'var(--akml-syntax-string)' },
        { tag: [t.number, t.bool, t.null], color: 'var(--akml-syntax-number)' },
        { tag: [t.comment, t.lineComment, t.blockComment], color: 'var(--akml-syntax-comment)', fontStyle: 'italic' },
        { tag: [t.function(t.variableName), t.function(t.propertyName), t.standard(t.name)], color: 'var(--akml-syntax-function)' },
        { tag: [t.typeName, t.propertyName, t.className, t.attributeName], color: 'var(--akml-syntax-tablecolumn)' },
    ]);

    // T109 follow-up: route CodeMirror's autocomplete through ICompletionService
    // on the .NET side so the popup is fed by the bridge (online) or the cached
    // schema snapshot (offline). Returns null when the .NET side gives an empty
    // list -- CM hides the popup automatically in that case.
    //
    // Trigger contexts (each independently opens the popup):
    //   1. Caret sits on or right after an identifier (CM's "typing" flow):
    //      replace text starts at the word boundary, CM fuzzy-filters by prefix.
    //   2. Caret is right after whitespace following an SQL keyword that
    //      grammatically expects an expression next (WHERE / AND / OR / FROM /
    //      JOIN / ON / SET / HAVING / SELECT / GROUP BY / ORDER BY, and — spec
    //      032 FR-002 — the DML verbs UPDATE / INSERT [INTO] / DELETE [FROM] /
    //      EXEC(UTE)): show the full candidate list anchored at the caret, no
    //      replacement range. This is the case the user reported -- typing
    //      "... AND " (trailing space) should suggest columns / tables without
    //      forcing Ctrl+Space.
    //   3. Caret is right after a member-access dot (`o.` / `dbo.` / `[Sales].` /
    //      `"dbo".`) — spec 032 FR-001: the single most common completion gesture.
    //      The leading char class requires an identifier START (letter/_/]/"),
    //      so numeric literals (`3.`) never trigger.
    //   4. context.explicit === true (Ctrl+Space) overrides everything.
    //
    // PR #236 review: regex hoisted above updateListener (was previously below
    // the listener and worked only because the closure binding resolved at
    // first-firing time — a code smell that would break if the listener could
    // fire synchronously during view construction).
    // `in` intentionally omitted: typing "WHERE x IN " almost always wants a
    // subquery / value list, not a column name; the false-positive popup was
    // more noise than help.
    // Spec 042: the trigger words and the word pattern follow the editor's current database kind.
    const currentKind = () => { const i = _instances.get(hostElementId); return i && i.kind ? i.kind : (options && options.dialect); };
    const DOT_MEMBER_TRIGGER = /(?:[A-Za-z_\]"][\w$]*|\]|")\.$/;

    // Spec 041 (R65, FR-083): the caret for the status bar ("Ln 1, Col 12"), throttled to one report per 100 ms.
    let caretTimer = 0;
    let caretState = null;
    const reportCaret = (state) => {
        caretState = state;
        if (caretTimer) return;
        caretTimer = setTimeout(() => {
            caretTimer = 0;
            const pos = caretPosition(caretState.doc, caretState.selection.main.head);
            dotNetRef.invokeMethodAsync('OnSelectionChangedFromJs', pos.line, pos.column).catch(() => { /* page gone */ });
        }, 100);
    };

    const updateListener = cm.view.EditorView.updateListener.of((update) => {
        if (dotNetRef && (update.selectionSet || update.docChanged)) reportCaret(update.state);
        if (!update.docChanged || !dotNetRef) return;

        const text = update.state.doc.toString();
        // Fire-and-forget; the C# side debounces. Spec 043: with the query tab shown now, so a report that arrives after a
        // tab switch is still filed under the tab it was typed in.
        dotNetRef.invokeMethodAsync('OnTextChangedFromJs', text, _instances.get(hostElementId)?.currentTab ?? null);

        // PR #236 review: only consider user-typed transactions for the
        // post-keyword trigger. Programmatic dispatches (Format → setText,
        // refactoring previews, etc.) shouldn't pop the autocomplete just
        // because the formatted SQL happens to end with "WHERE foo = 1 AND ".
        // CM transactions carry a userEvent annotation when they originate
        // from user input ("input.type" / "input.paste" / "delete.*").
        const userTyped = update.transactions.some(t => {
            const ev = t.annotation(cm.state.Transaction.userEvent);
            return typeof ev === 'string' && ev.startsWith('input.');
        });
        if (!userTyped) return;

        // T109 follow-up Issue 1: when the user types a space (or any
        // non-identifier char) immediately after a trigger keyword, CM's
        // activateOnTyping doesn't fire because the typed char isn't a word
        // char. Detect that case and manually open the popup so the user
        // doesn't have to press Ctrl+Space after "WHERE ", "AND ", etc.
        try {
            let typedNonWord = false;
            update.changes.iterChanges((_fromA, _toA, _fromB, _toB, inserted) => {
                const ins = inserted.toString();
                if (ins.length > 0 && typedTriggerCharacters(currentKind()).test(ins[ins.length - 1])) {
                    typedNonWord = true;
                }
            });
            if (typedNonWord) {
                const pos = update.state.selection.main.head;
                const line = update.state.doc.lineAt(pos);
                const lineUpToCaret = line.text.slice(0, pos - line.from);
                // Spec 032 FR-001: '.' is a non-word char, so activateOnTyping never fires
                // for member access — this manual open is the ONLY dot-trigger path.
                if (postKeywordTrigger(currentKind()).test(lineUpToCaret) || DOT_MEMBER_TRIGGER.test(lineUpToCaret)) {
                    cm.autocomplete.startCompletion(update.view);
                }
            }
        } catch { /* never let trigger detection break the editor */ }
    });

    // Spec 043 (review of US1): the query tab shown now. A reply to a request made in one tab is dropped when another tab is
    // shown by the time it arrives (completion, ghost text, wildcard expansion, quick info, signature help).
    const shownTab = () => _instances.get(hostElementId)?.currentTab ?? null;

    const completionSource = async (context) => {
        if (!dotNetRef) return null;

        // Spec 032 FR-005 (C5): include '@'/'#' in the replace span so accepting
        // '@CustomerID' over a typed '@C' (or '#t' over '#') replaces the whole
        // sigil token instead of doubling it ('@@CustomerID').
        const kind = currentKind();
        const lineAtCaret = context.state.doc.lineAt(context.pos);
        const found = wordBefore(kind, lineAtCaret.text.slice(0, context.pos - lineAtCaret.from));
        const word = found ? { from: lineAtCaret.from + found.from, to: context.pos, text: found.text } : null;
        // Spec 042 (FR-061, scenario US3-7): PostgreSQL has no @variables, #temp tables or [brackets]. A popup the editor opened
        // by itself (after `SELECT `) is an explicit session that CodeMirror re-queries on every keystroke, so the sigil
        // has to end it here, or the list would stay open over the character that makes no sense.
        if (isPostgresKind(kind) && /[@#\[]$/.test(lineAtCaret.text.slice(0, context.pos - lineAtCaret.from))) return null;
        // A `;` ends the statement: the popup never opens on it, and a session already open (after `SELECT `) ends with it
        // instead of offering every statement keyword over the end of the line.
        if (endsStatement(lineAtCaret.text.slice(0, context.pos - lineAtCaret.from))) return null;
        const wordValid = word && (word.from !== word.to || context.explicit);

        // Detect "after a trigger keyword + whitespace" or "after a member-access
        // dot" (spec 032 FR-001) by looking at the line text leading up to the
        // caret. Cheaper than scanning the whole document.
        let postKeyword = false;
        let dotMember = false;
        if (!wordValid) {
            const lineUpToCaret = context.state.doc.lineAt(context.pos)
                .text.slice(0, context.pos - context.state.doc.lineAt(context.pos).from);
            postKeyword = postKeywordTrigger(kind).test(lineUpToCaret);
            dotMember = DOT_MEMBER_TRIGGER.test(lineUpToCaret);
        }

        if (!wordValid && !postKeyword && !dotMember && !context.explicit) return null;

        try {
            // Pass the LIVE document text so the offline smart GROUP BY action parses what the
            // user is actually editing (the debounced session would be stale during fast typing).
            // The online path ignores it; it never reaches the engine wire.
            // Spec 042: a reply that arrives after the user typed on describes text that is gone (the popup opened by
            // `SELECT ` must not reappear over `SELECT @`). PostgreSQL drops it; SQL Server keeps its behaviour.
            let stale = false;
            if (isPostgresKind(kind)) context.addEventListener('abort', () => { stale = true; }, { onDocChange: true });
            const tabAtRequest = shownTab();
            const items = await dotNetRef.invokeMethodAsync(
                'RequestCompletionsFromJs', context.pos, context.state.doc.toString());
            if (stale || context.aborted || shownTab() !== tabAtRequest) return null;
            if (!items || items.length === 0) return null;
            // Spec 042: while a quoted name is being typed CodeMirror matches the text from the opening quote, so the
            // label is the quoted form (a label without the quote can never match `"Aud`).
            const quotedPrefix = isPostgresKind(kind) && word && word.text.startsWith('"');
            return {
                from: wordValid ? word.from : context.pos,
                options: items.map(i => {
                    const type = i.type || 'text';
                    // Smart-action items (SQL-Prompt-style "▶ Add columns from SELECT") must sort
                    // to the top: CM6 orders an empty-prefix popup by label, and "▶" (U+25B6)
                    // sorts after letters, which would otherwise bury the action at the bottom.
                    // Map the engine's SortPriority (ASCENDING — lower ranks first) to CM6's
                    // boost (DESCENDING — higher ranks first) so the web popup order matches the
                    // desktop (e.g. data types before constraint keywords in a CREATE TABLE column).
                    // ▶ smart-actions keep the top slot. The /10 scale keeps boost a gentle
                    // tiebreaker: it fully orders the empty-prefix popup but does not override CM's
                    // fuzzy-match score once a prefix is typed (mirrors the engine: score, then
                    // SortPriority).
                    const boost = (i.label && i.label.charCodeAt(0) === 0x25B6)
                        ? 99
                        : (typeof i.sortPriority === 'number' ? (100 - i.sortPriority) / 10 : undefined);
                    // Spec 027 T011: snippet items expand (with tab-stops) on accept instead
                    // of inserting their text literally. A function `apply` is CM6's accept
                    // hook; non-snippet items keep the plain-string `apply` (literal insert).
                    if (type === 'snippet') {
                        return {
                            label: i.label,
                            type,
                            detail: i.detail || undefined,
                            boost,
                            apply: (view, _completion, from, to) =>
                                applySnippetBody(cm, view, i.insertText, from, to),
                        };
                    }
                    // A bracketed name ("[Order Details]") must not double a bracket the user has
                    // already typed. Plain-string apply replaces only the word CM matched, which
                    // for `[Ord` is "Ord" — leaving the typed "[" in place and producing
                    // "[[Order Details]". applyBracketed widens the replacement to take in an
                    // open "[" (and an auto-closed "]") so the name goes in exactly once.
                    const insertText = i.insertText;
                    if (quotedPrefix && typeof insertText === 'string' && insertText.startsWith('"')) {
                        return {
                            label: insertText,
                            apply: (view, _completion, from, to) => {
                                const span = quotedReplaceSpan(view.state.doc.toString(), from, to, insertText);
                                view.dispatch({ changes: { from: span.from, to: span.to, insert: insertText }, selection: { anchor: span.from + insertText.length }, userEvent: 'input.complete' });
                            },
                            type,
                            detail: i.detail || undefined,
                            boost,
                        };
                    }
                    return {
                        label: i.label,
                        apply: (!isPostgresKind(kind) && typeof insertText === 'string' && insertText.includes('['))
                            ? (view, _completion, from, to) => applyBracketed(view, insertText, from, to)
                            : insertText,
                        type,
                        detail: i.detail || undefined,
                        boost,
                    };
                }),
                // CM does its own fuzzy filter against the prefix at `from`. The
                // empty-prefix case (post-keyword trigger) re-invokes the source
                // as soon as the user types a non-word char, which is what we want.
                validFor: completionValidFor(kind),
            };
        } catch {
            // Never let a .NET interop crash kill the popup.
            return null;
        }
    };

    // ── Spec 028 (M6) US5 — inline ghost text (hand-rolled, no new package) ───────────────
    // Per-instance config; mutated by setGhostTextConfig from EditorComponent.
    const ghost = { enabled: false, delayMs: 350, timer: null, reqId: 0 };

    class GhostWidget extends cm.view.WidgetType {
        constructor(text) { super(); this.text = text; }
        eq(other) { return other.text === this.text; }
        toDOM() {
            const span = document.createElement('span');
            span.className = 'akml-ghost-text';
            span.textContent = this.text;
            span.style.opacity = '0.45';
            span.style.fontStyle = 'italic';
            span.style.pointerEvents = 'none';
            span.style.whiteSpace = 'pre';
            return span;
        }
    }

    const setGhost = cm.state.StateEffect.define();
    const ghostField = cm.state.StateField.define({
        create() { return cm.view.Decoration.none; },
        update(deco, tr) {
            if (tr.docChanged) deco = cm.view.Decoration.none;   // dismiss-on-type
            deco = deco.map(tr.changes);
            for (const e of tr.effects) {
                if (e.is(setGhost)) {
                    deco = e.value
                        ? cm.view.Decoration.set([
                            cm.view.Decoration.widget({ widget: new GhostWidget(e.value.text), side: 1 }).range(e.value.pos),
                          ])
                        : cm.view.Decoration.none;
                }
            }
            return deco;
        },
        provide: f => cm.view.EditorView.decorations.from(f),
    });

    function currentGhost(view) {
        const deco = view.state.field(ghostField, false);
        if (!deco || deco.size === 0) return null;
        let found = null;
        deco.between(0, view.state.doc.length, (from, _to, value) => {
            const w = value.widget;
            if (w && typeof w.text === 'string') found = { from, text: w.text };
        });
        return found;
    }

    function acceptGhost(view) {
        const g = currentGhost(view);
        if (!g) return false;   // fall through to autocomplete-accept / snippet-tab / indentWithTab
        // An open completion list owns Tab, as in SSMS: Tab commits the highlighted item, never the ghost behind it
        // (reported 2026-10-22: Tab in the column list put in a space — the ghost text — instead of the column).
        if (cm.autocomplete.completionStatus(view.state) === 'active') return false;
        view.dispatch({
            changes: { from: g.from, insert: g.text },
            selection: { anchor: g.from + g.text.length },
            effects: setGhost.of(null),
        });
        return true;
    }

    function dismissGhost(view) {
        if (!currentGhost(view)) return false;
        view.dispatch({ effects: setGhost.of(null) });
        return true;
    }

    function ghostShouldFire(state) {
        const pos = state.selection.main.head;
        try { if (cm.autocomplete.completionStatus(state) !== null) return false; } catch { /* ignore */ }
        const line = state.doc.lineAt(pos);
        if (line.text.trim().length === 0) return false;   // empty line
        if (pos !== line.to) return false;                 // only at end of line
        try {
            let node = cm.language.syntaxTree(state).resolveInner(pos, -1);
            while (node) {
                if (node.name === 'LineComment' || node.name === 'BlockComment' ||
                    node.name === 'String' || node.name === 'QuotedIdentifier') return false;
                node = node.parent;
            }
        } catch { /* tree not ready -> don't suppress on that basis */ }
        return true;
    }

    function requestGhost(view) {
        if (!ghost.enabled || !dotNetRef) return;
        if (!ghostShouldFire(view.state)) return;
        const pos = view.state.selection.main.head;
        const docText = view.state.doc.toString();
        const reqId = ++ghost.reqId;
        const tabAtRequest = shownTab();
        dotNetRef.invokeMethodAsync('RequestGhostTextFromJs', pos, docText).then(suggestion => {
            if (reqId !== ghost.reqId || !suggestion) return;      // superseded or empty
            const inst = _instances.get(hostElementId);
            if (!inst || inst.currentTab !== tabAtRequest) return;  // another query tab is shown now
            if (inst.view.state.selection.main.head !== pos) return; // staleness check
            inst.view.dispatch({ effects: setGhost.of({ text: suggestion, pos }) });
        }).catch(() => { /* ghost text never surfaces an error */ });
    }

    const ghostListener = cm.view.EditorView.updateListener.of((update) => {
        if (!ghost.enabled || !update.docChanged) return;
        const userTyped = update.transactions.some(t => {
            const ev = t.annotation(cm.state.Transaction.userEvent);
            return typeof ev === 'string' && ev.startsWith('input.');
        });
        if (!userTyped) return;
        if (ghost.timer) clearTimeout(ghost.timer);
        ghost.timer = setTimeout(() => requestGhost(update.view), ghost.delayMs);
    });

    // Prec.highest so Tab/Escape are consulted first; the handlers return false (fall through)
    // unless a ghost suggestion is actually showing, leaving autocomplete + snippet Tab intact.
    const ghostKeymap = cm.state.Prec.highest(cm.view.keymap.of([
        { key: 'Tab', run: acceptGhost },
        { key: 'Escape', run: dismissGhost },
    ]));

    // ── Spec 030 — expand "SELECT *" on Tab (SQL Prompt parity) ──────────────────────────────────
    // When the caret sits at a wildcard ('*' or 'alias.*') inside a SELECT, Tab asks the engine
    // (via .NET ExpandWildcardFromJs) for the FROM tables' columns and replaces the wildcard with
    // the column list. Returns false (falls through to accept-completion / snippet / indent)
    // whenever it isn't a wildcard (wildcardAtCaret), so ordinary Tab is unaffected.
    function expandWildcard(view) {
        if (!dotNetRef) return false;
        const keyTime = nowMs();
        // An open completion popup owns Tab (accept the highlighted item) — don't steal it.
        if (cm.autocomplete.completionStatus(view.state) === 'active') return false;
        const wildcard = wildcardAtCaret(cm, view.state, currentKind());
        if (!wildcard) return false;                                  // not a wildcard → normal Tab

        const pos = wildcard.caretOffset;   // just after the '*', whichever side of it the caret was on
        // Snapshot the keypress-time document (CM6 Text is immutable, so this is a cheap reference).
        // The engine computes spanStart/spanLength against THIS text; the guard below re-verifies the
        // span against the live document after the async round-trip.
        const docAtRequest = view.state.doc;
        const tabAtRequest = shownTab();
        const atKey = { time: keyTime, tab: tabAtRequest, doc: docAtRequest, selection: view.state.selection };
        // Spec 043 follow-up (review): a decline that comes back at once (not connected, a PostgreSQL refusal, the schema
        // not loaded yet) does what Tab would have done — the next snippet field, else indent — while nothing has moved
        // since the key (declinedTabMayIndent). A failed call is a decline too.
        const declined = () => {
            const inst = _instances.get(hostElementId);
            if (!inst) return;
            const live = inst.view.state;
            if (!declinedTabMayIndent(atKey, { time: nowMs(), tab: inst.currentTab ?? null, doc: live.doc, selection: live.selection })) return;
            const nextField = cm.autocomplete.nextSnippetField;
            if (!(typeof nextField === 'function' && nextField(inst.view))) cm.commands.indentMore(inst.view);
        };
        dotNetRef.invokeMethodAsync('ExpandWildcardFromJs', pos, docAtRequest.toString())
            .then(res => {
                if (!res || !res.ok) { declined(); return; }
                const inst = _instances.get(hostElementId);
                if (!inst || inst.currentTab !== tabAtRequest) return;   // another query tab is shown now
                const end = res.spanStart + res.spanLength;
                if (end > docAtRequest.length) return;                // engine span outside its own snapshot
                const doc = inst.view.state.doc;
                if (end > doc.length) return;                         // document shrank under us
                // Stale-document guard: the expansion replaces the exact text the engine saw at
                // [spanStart, end). If the user typed during the round-trip and that span no longer
                // matches, dispatching would clobber their edit — abort silently instead.
                if (doc.sliceString(res.spanStart, end) !== docAtRequest.sliceString(res.spanStart, end)) return;
                inst.view.dispatch({
                    changes: { from: res.spanStart, to: end, insert: res.text },
                    selection: { anchor: res.spanStart + res.text.length },
                    userEvent: 'input.complete',
                });
            })
            .catch(() => { try { declined(); } catch { /* best-effort — a failed expansion just leaves the '*' in place */ } });
        return true;   // handled — swallow the Tab (the expansion is applied asynchronously)
    }

    const wildcardKeymap = cm.state.Prec.highest(cm.view.keymap.of([
        { key: 'Tab', run: expandWildcard },
    ]));

    // Spec 032 (FR-003, campaign finding 3) — Tab accepts the highlighted completion. CM6's
    // completion keymap deliberately ships NO Tab binding, so without this Tab fell through to
    // indentWithTab while the popup was open. acceptCompletion returns false when no completion
    // is active, preserving the chain: ghost-accept → completion-accept → wildcard-expand → indent.
    // Registered between ghostKeymap and wildcardKeymap in the extensions list (same Prec class;
    // earlier registration wins).
    const completionAcceptKeymap = cm.state.Prec.highest(cm.view.keymap.of([
        { key: 'Tab', run: cm.autocomplete.acceptCompletion },
    ]));

    // ── Spec 030 (web Phase 1) — Quick Info on hover ─────────────────────────────────────────
    // CM6 hoverTooltip extension: asks the .NET IQuickInfoService for the object/column under the
    // hovered position and renders a small themed tooltip. Returns null = no tooltip.
    const quickInfoHover = cm.view.hoverTooltip(async (view, pos) => {
        if (!dotNetRef) return null;
        let info;
        const tabAtRequest = shownTab();
        try { info = await dotNetRef.invokeMethodAsync('RequestQuickInfoFromJs', pos, view.state.doc.toString()); }
        catch { return null; }
        if (!info || shownTab() !== tabAtRequest) return null;
        const wr = view.state.wordAt(pos);
        const from = wr ? wr.from : pos;
        const to = wr ? wr.to : pos;
        return {
            pos: from, end: to, above: true,
            create() {
                const dom = document.createElement('div');
                dom.className = 'akml-qi-tooltip';
                const header = document.createElement('div');
                header.className = 'akml-qi-header';
                header.textContent = (info.objectType ? `[${info.objectType}] ` : '') + (info.header || '');
                dom.appendChild(header);
                if (info.description) {
                    const desc = document.createElement('div');
                    desc.className = 'akml-qi-desc';
                    desc.textContent = info.description;
                    dom.appendChild(desc);
                }
                if (info.details && info.details.length) {
                    const list = document.createElement('div');
                    list.className = 'akml-qi-details';
                    for (const d of info.details) {
                        const row = document.createElement('div');
                        const link = documentationLink(d);
                        if (link) {
                            // Spec 042 (FR-053): the manual page opens in its own tab.
                            row.appendChild(document.createTextNode(d.label + ': '));
                            const a = document.createElement('a');
                            a.href = link;
                            a.target = '_blank';
                            a.rel = 'noopener noreferrer';
                            a.textContent = link.replace('https://www.postgresql.org/', '');
                            a.className = 'akml-qi-link';
                            row.appendChild(a);
                        } else {
                            row.textContent = d.value ? `${d.label}: ${d.value}` : d.label;
                            // a column list or a syntax diagram keeps its line breaks
                            if (d.value && d.value.indexOf('\n') >= 0) row.style.whiteSpace = 'pre-wrap';
                        }
                        list.appendChild(row);
                    }
                    dom.appendChild(list);
                }
                return { dom };
            },
        };
    }, { hoverTime: 350 });

    // ── Spec 030 (web Phase 1) — Signature help (parameter hints) ─────────────────────────────
    // A single themed tooltip driven by a StateField, shown when the caret is inside a proc/function
    // call. Re-requested when the user types '(' or ',' (or types further while one is showing);
    // dismissed on ')' / Escape / when the service reports no call site.
    const setSig = cm.state.StateEffect.define();
    const sigField = cm.state.StateField.define({
        create() { return null; },
        update(value, tr) {
            for (const e of tr.effects) if (e.is(setSig)) value = e.value;
            return value;
        },
        provide: f => cm.view.showTooltip.from(f),
    });

    // Monotonic request sequence: each requestSignature claims the next value; its async response
    // is applied only if no newer request OR dismissal happened meanwhile — otherwise a slow
    // round-trip could re-show a stale tooltip after the user already typed ')' / moved on.
    let sigSeq = 0;

    async function requestSignature(view) {
        if (!dotNetRef) return;
        const seq = ++sigSeq;
        const pos = view.state.selection.main.head;
        const tabAtRequest = shownTab();
        let sig;
        try { sig = await dotNetRef.invokeMethodAsync('RequestSignatureHelpFromJs', pos, view.state.doc.toString()); }
        catch { return; }
        if (seq !== sigSeq) return;   // superseded by a newer request or a dismissal
        const inst = _instances.get(hostElementId);
        if (!inst || inst.currentTab !== tabAtRequest) return;   // another query tab is shown now
        if (!sig) { inst.view.dispatch({ effects: setSig.of(null) }); return; }
        const tooltip = {
            pos: inst.view.state.selection.main.head,
            above: true,
            create() {
                const dom = document.createElement('div');
                dom.className = 'akml-sig-tooltip';
                const label = document.createElement('div');
                label.className = 'akml-sig-label';
                label.textContent = sig.label || '';
                dom.appendChild(label);
                if (sig.parameters && sig.parameters.length) {
                    const ap = Math.max(0, Math.min(sig.activeParameter, sig.parameters.length - 1));
                    const p = sig.parameters[ap];
                    const active = document.createElement('div');
                    active.className = 'akml-sig-active';
                    active.textContent = (p.name || '') + (p.type ? ' ' + p.type : '');
                    dom.appendChild(active);
                }
                if (sig.documentation) {
                    const doc = document.createElement('div');
                    doc.className = 'akml-sig-doc';
                    doc.textContent = sig.documentation;
                    dom.appendChild(doc);
                }
                return { dom };
            },
        };
        inst.view.dispatch({ effects: setSig.of(tooltip) });
    }

    function dismissSignature(view) {
        sigSeq++;   // invalidate any in-flight requestSignature so its stale response is dropped
        if (view.state.field(sigField, false)) { view.dispatch({ effects: setSig.of(null) }); return true; }
        return false;
    }

    const signatureListener = cm.view.EditorView.updateListener.of((update) => {
        if (!update.docChanged || !dotNetRef) return;
        const userTyped = update.transactions.some(t => {
            const ev = t.annotation(cm.state.Transaction.userEvent);
            return typeof ev === 'string' && ev.startsWith('input.');
        });
        if (!userTyped) return;
        let lastChar = '';
        update.changes.iterChanges((_fa, _ta, _fb, _tb, inserted) => {
            const s = inserted.toString();
            if (s.length > 0) lastChar = s[s.length - 1];
        });
        if (lastChar === ')') { dismissSignature(update.view); return; }
        const showing = update.state.field(sigField, false) != null;
        if (lastChar === '(' || lastChar === ',' || showing) {
            requestSignature(update.view);
        }
    });

    // ── Spec 030 (web Phase 1) — Go to definition (F12) → peek panel ──────────────────────────
    function removePeekPanel(host) {
        if (host && host._akmlPeek) { host._akmlPeek.remove(); host._akmlPeek = null; }
    }

    function showPeekPanel(host, def) {
        removePeekPanel(host);
        const panel = document.createElement('div');
        panel.className = 'akml-peek-panel';
        const header = document.createElement('div');
        header.className = 'akml-peek-header';
        const title = document.createElement('span');
        title.textContent = (def.found
            ? ((def.objectType ? def.objectType + ': ' : '') + (def.fullName || 'Definition'))
            : (def.message || 'No definition found.'));
        const close = document.createElement('button');
        close.className = 'akml-peek-close';
        close.type = 'button';
        close.textContent = '✕';
        close.onclick = () => removePeekPanel(host);
        header.appendChild(title);
        header.appendChild(close);
        panel.appendChild(header);
        if (def.found && def.definition) {
            const pre = document.createElement('pre');
            pre.className = 'akml-peek-body';
            pre.textContent = def.definition;
            panel.appendChild(pre);
        }
        host.appendChild(panel);
        host._akmlPeek = panel;
    }

    function gotoDefinition(view) {
        if (!dotNetRef) return true;
        const pos = view.state.selection.main.head;
        const tabAtRequest = shownTab();
        dotNetRef.invokeMethodAsync('RequestGoToDefinitionFromJs', pos, view.state.doc.toString())
            .then(def => {
                if (shownTab() !== tabAtRequest) return;   // another query tab is shown now: its definition is not this one
                const host = document.getElementById(hostElementId);
                if (host && def) showPeekPanel(host, def);
            })
            .catch(() => { /* go-to-def never surfaces an error into typing */ });
        return true;   // handled (the work is async)
    }

    // Spec 030 — Phase 5: Ctrl/Cmd+Enter runs the query. Registered INSIDE the CM6 keymap (not just
    // the Blazor page handler) because CM6 has focus and swallows Mod-Enter. The Blazor side surfaces
    // it via EditorComponent.ExecuteFromJs → OnExecute → Editor.ExecuteAsync. F5 is NOT bound here:
    // since spec 041 (R37) the editor page claims F5, Ctrl+F5, Ctrl+R and the Ctrl+K chords in
    // akml-workspace.js's document-level capture listener, before CodeMirror or the browser see them.
    function runExecute(view) {
        if (!dotNetRef) return false;
        dotNetRef.invokeMethodAsync('ExecuteFromJs')
            .catch(() => { /* execution errors surface in the results pane, never into the editor */ });
        return true;   // handled (the work is async); stops CM inserting a newline.
    }

    const navKeymap = cm.view.keymap.of([
        { key: 'F12', run: gotoDefinition },
        { key: 'Escape', run: dismissSignature },   // also clears a stray signature tooltip
        // Mod-Enter moved into the main keymap AHEAD of defaultKeymap (spec 032 FR-004):
        // registered here (after the defaultKeymap spread) it was shadowed by defaultKeymap's
        // own Mod-Enter → insertBlankLine, leaving no keyboard execute at all (campaign finding 4).
    ]);

    // Spec 041 (R53): word wrap and tab size live in compartments, so Settings › Editor reconfigures the open
    // editor (setEditorOptions) instead of recreating it.
    const wrapCompartment = new cm.state.Compartment();
    const tabCompartment = new cm.state.Compartment();
    const dialectCompartment = new cm.state.Compartment();   // Spec 042: SQL Server (default) or PostgreSQL language
    const optionExt = optionExtensions(cm, options);
    applyEditorAppearance(host, options);

    // Spec 043 (R6): kept on the instance, so every query tab's state is built with the same extensions (switchTab).
    const extensions = [
        writingAssistantOptOut(cm),   // Spec 043 (FR-036): Grammarly and LanguageTool stay out of the SQL
        wrapCompartment.of(optionExt.wrap),
        tabCompartment.of(optionExt.tab),
        // The undo history. historyKeymap below binds Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z, but without this extension
        // there is no history for them to act on, and undo did nothing anywhere in the editor.
        cm.commands.history(),
        cm.view.lineNumbers(),
        cm.view.highlightActiveLine(),
        cm.language.bracketMatching(),   // Spec 043 (FR-019): the bracket matching the one at the caret is outlined
        ghostKeymap,
        completionAcceptKeymap,   // Spec 032 FR-003 — Tab accepts an open completion (falls through when closed)
        wildcardKeymap,   // Tab-expand SELECT * (falls through when not a wildcard)
        cm.view.keymap.of([
            // Spec 032 FR-004 — must precede defaultKeymap's own Mod-Enter (insertBlankLine),
            // which otherwise shadows it: this is the only working keyboard execute.
            { key: 'Mod-Enter', run: runExecute },
            ...cm.commands.defaultKeymap,
            ...cm.commands.historyKeymap,
            ...cm.search.searchKeymap,
            cm.commands.indentWithTab,
        ]),
        dialectCompartment.of(dialectExtension(cm, options && options.dialect)),
        // Token-driven SQL syntax theme (--akml-syntax-*). fallback:true keeps any tag we didn't
        // map rendering via CM6's default so nothing goes uncoloured.
        cm.language.syntaxHighlighting(akmlHighlightStyle, { fallback: true }),
        cm.language.syntaxHighlighting(cm.language.defaultHighlightStyle, { fallback: true }),
        cm.autocomplete.autocompletion({
            override: [completionSource],
            activateOnTyping: true,
            maxRenderedOptions: 50,
        }),
        cm.lint.lintGutter(),
        ghostField,
        ghostListener,
        // Spec 030 (web Phase 1) — IntelliSense parity: hover quick-info, signature help, F12 goto.
        quickInfoHover,
        sigField,
        signatureListener,
        navKeymap,
        updateListener,
    ];
    const state = cm.state.EditorState.create({ doc: initialText ?? '', extensions });

    const view = new cm.view.EditorView({
        state,
        parent: host,
    });

    const disposeLintKeyboard = installLintKeyboard(view, hostElementId);
    _instances.set(hostElementId, {
        view, cm, dotNetRef, ghost, wrapCompartment, tabCompartment, dialectCompartment, disposeLintKeyboard, kind: options && options.dialect,
        // Spec 043 (R6): the query tabs. The view shows `currentTab`; every other tab's state waits in `tabs`.
        extensions, optionExt, tabs: new Map(), currentTab: (options && options.tabId) || null,
    });
}

// CodeMirror's lint gutter opens its own tooltip on mouseover. Give its markers the same
// interaction from the keyboard, and let its existing mouse-leave logic dismiss it on Escape.
function installLintKeyboard(view, hostElementId) {
    if (typeof MutationObserver === 'undefined' || !view.dom) return () => {};
    let focused = null;
    const close = () => {
        focused?.dispatchEvent(new MouseEvent('mouseout'));
        focused?.removeAttribute('aria-describedby');
        window.dispatchEvent(new MouseEvent('mousemove', { clientX: -100, clientY: -100 }));
    };
    const decorate = () => {
        for (const marker of view.dom.querySelectorAll('.cm-lint-marker')) {
            marker.tabIndex = 0;
            marker.setAttribute('aria-label', 'SQL diagnostics');
        }
        const tip = view.dom.querySelector('.cm-tooltip-lint');
        if (focused && tip) {
            tip.id = `${hostElementId}-lint-help`;
            tip.setAttribute('role', 'tooltip');
            focused.setAttribute('aria-describedby', tip.id);
        }
    };
    const focus = e => {
        if (!e.target.matches('.cm-lint-marker')) return;
        focused = e.target;
        focused.dispatchEvent(new MouseEvent('mouseover'));
    };
    const blur = () => { close(); focused = null; };
    const key = e => { if (e.key === 'Escape') close(); };
    const observer = new MutationObserver(decorate);
    observer.observe(view.dom, { childList: true, subtree: true });
    view.dom.addEventListener('focusin', focus);
    view.dom.addEventListener('focusout', blur);
    view.dom.addEventListener('keydown', key);
    return () => {
        close(); observer.disconnect();
        view.dom.removeEventListener('focusin', focus);
        view.dom.removeEventListener('focusout', blur);
        view.dom.removeEventListener('keydown', key);
    };
}

// ── Spec 043 (research R6, spike S2) — query tabs: one view, one EditorState per tab ─────────────────────────
// The view shows one tab; every other tab's state (text, selection, undo history, findings, an open completion) waits in
// inst.tabs. view.setState runs no update listener, so a swap never reports a text change and never marks a tab modified.

/**
 * Pure: put the shown tab's snapshot ({state, scrollTop, scrollLeft}) into `tabs` under `fromTabId` (nothing when there
 * was none) and take `toTabId`'s out of it, or a new snapshot from `createState()` for a tab never shown. The shown tab
 * is never in the map.
 */
export function stashAndPick(tabs, fromTabId, fromSnapshot, toTabId, createState) {
    if (fromTabId && fromSnapshot) tabs.set(fromTabId, fromSnapshot);
    const stored = tabs.get(toTabId);
    if (stored) {
        tabs.delete(toTabId);
        return stored;
    }
    return { state: createState(), scrollTop: 0, scrollLeft: 0 };
}

/** Pure: `state` with its whole text replaced (one undoable change). */
export function replaceDoc(state, text) {
    return state.update({ changes: { from: 0, to: state.doc.length, insert: text ?? '' } }).state;
}

/** Pure: replaceDoc only when the text is still exactly `expected` (a Format reply); null when it moved on. */
export function replaceDocIfUnchanged(state, expected, text) {
    if (!state || state.doc.toString() !== (expected ?? '')) return null;
    return replaceDoc(state, text);
}

/** Pure: `state` showing `diags` (see toLintDiagnostics) instead of what it showed. */
export function withDiagnostics(cm, state, diags) {
    if (!cm.lint || typeof cm.lint.setDiagnostics !== 'function') return state;
    return state.update(cm.lint.setDiagnostics(state, toLintDiagnostics(state.doc, diags))).state;
}

/**
 * The stored snapshot of a tab that is not shown; null when `tabId` is the shown one (or none is named); undefined when
 * the tab has never been shown here and `create` is false. Only a whole new text (`setText`) makes a state for such a tab:
 * an empty state made for anything else would be picked over the page's text when the tab is first shown.
 */
function storedTab(inst, tabId, create = false) {
    if (!tabId || tabId === inst.currentTab) return null;
    let stored = inst.tabs.get(tabId);
    if (!stored) {
        if (!create) return undefined;
        stored = { state: inst.cm.state.EditorState.create({ doc: '', extensions: inst.extensions }), scrollTop: 0, scrollLeft: 0 };
        inst.tabs.set(tabId, stored);
    }
    return stored;
}

/**
 * Show query tab `tabId`: the shown tab's state is stored, `tabId`'s comes back (or is made from `seedText` the first
 * time), with the current editor options and `kind`'s SQL language.
 */
export function switchTab(hostElementId, tabId, seedText, kind) {
    const inst = _instances.get(hostElementId);
    if (!inst || !tabId || inst.currentTab === tabId) return;
    const view = inst.view;
    const shown = inst.currentTab
        ? { state: view.state, scrollTop: view.scrollDOM.scrollTop, scrollLeft: view.scrollDOM.scrollLeft }
        : null;
    const next = stashAndPick(inst.tabs, inst.currentTab, shown, tabId,
        () => inst.cm.state.EditorState.create({ doc: seedText ?? '', extensions: inst.extensions }));
    inst.currentTab = tabId;
    inst.kind = kind;
    removePeek(hostElementId);   // a definition peeked in one tab is not shown over another
    view.setState(next.state);
    // A stored state keeps the compartments it was made with: bring them to today's options and this tab's kind.
    view.dispatch({ effects: [
        inst.dialectCompartment.reconfigure(dialectExtension(inst.cm, kind)),
        inst.wrapCompartment.reconfigure(inst.optionExt.wrap),
        inst.tabCompartment.reconfigure(inst.optionExt.tab),
    ] });
    view.scrollDOM.scrollTop = next.scrollTop;
    view.scrollDOM.scrollLeft = next.scrollLeft;
}

/** Forget a closed tab's stored state. The page shows another tab first; closing the shown one leaves none current. */
export function closeTab(hostElementId, tabId) {
    const inst = _instances.get(hostElementId);
    if (!inst || !tabId) return;
    inst.tabs.delete(tabId);
    if (inst.currentTab === tabId) {
        inst.currentTab = null;
        removePeek(hostElementId);
    }
}

/** Spec 043 (review of US1): the Go to definition peek belongs to the tab it was opened in. */
function removePeek(hostElementId) {
    const host = typeof document === 'undefined' ? null : document.getElementById(hostElementId);
    if (host && host._akmlPeek) {
        host._akmlPeek.remove();
        host._akmlPeek = null;
    }
}

/**
 * Spec 042 (FR-060, FR-065) - switch an open editor's SQL language to a database kind ('postgresql' or
 * 'sqlserver'). Reconfigures one compartment: the text, caret, history and diagnostics are untouched, so switching
 * connections keeps what the user typed.
 */
export function setDialect(hostElementId, kind) {
    const inst = _instances.get(hostElementId);
    if (!inst || !inst.dialectCompartment) return;
    inst.kind = kind;
    inst.view.dispatch({ effects: inst.dialectCompartment.reconfigure(dialectExtension(inst.cm, kind)) });
}

/** Spec 041 (R53) — apply changed editor options to an open editor. */
export function setEditorOptions(hostElementId, options) {
    const inst = _instances.get(hostElementId);
    if (!inst) return;
    const ext = optionExtensions(inst.cm, options);
    inst.optionExt = ext;   // spec 043: a tab shown later gets the same options (switchTab)
    inst.view.dispatch({ effects: [inst.wrapCompartment.reconfigure(ext.wrap), inst.tabCompartment.reconfigure(ext.tab)] });
    applyEditorAppearance(document.getElementById(hostElementId), options);
}

/**
 * Spec 028 (M6) US5 — enable/disable inline ghost text and set its debounce. Called from
 * EditorComponent after create() with the persisted AiFeatureSettings.
 */
export function setGhostTextConfig(hostElementId, enabled, delayMs) {
    const inst = _instances.get(hostElementId);
    if (!inst || !inst.ghost) return;
    inst.ghost.enabled = !!enabled;
    if (typeof delayMs === 'number' && delayMs > 0) inst.ghost.delayMs = delayMs;
}

/** Return the current document text (spec 043: of query tab `tabId` when it is not the shown one). */
export function getText(hostElementId, tabId) {
    const inst = _instances.get(hostElementId);
    if (!inst) return '';
    const stored = storedTab(inst, tabId);
    if (stored === undefined) return '';   // never shown here: the page has its text
    return (stored ? stored.state : inst.view.state).doc.toString();
}

/**
 * Spec 028 (M6) — return the currently selected text (empty string when there is no
 * selection). The AI dock (Editor.razor) feeds this to the action panel so Explain / Fix /
 * etc. operate on the user's selection, falling back to the whole document when empty.
 */
export function getSelectedText(hostElementId) {
    const inst = _instances.get(hostElementId);
    if (!inst) return '';
    const sel = inst.view.state.selection.main;
    return inst.view.state.sliceDoc(sel.from, sel.to);
}

/**
 * Spec 041 (R24) — pure: the 1-based document line the executed text starts on. Execute runs the
 * selection, or the whole document when the selection is empty or only whitespace — then line 1.
 * @param {{lineAt:(pos:number)=>{number:number}, sliceString:(from:number,to:number)=>string}} doc  a CM Text
 * @param {{from:number, to:number}} range  the main selection
 */
export function selectionStartLine(doc, range) {
    if (!range || range.to <= range.from) return 1;
    if (doc.sliceString(range.from, range.to).trim().length === 0) return 1;
    return doc.lineAt(range.from).number;
}

/**
 * Spec 041 (R24) — the selected text and the line it starts on, read together so an error's server line
 * can be mapped back to the document line it came from.
 */
export function getSelectionInfo(hostElementId) {
    const inst = _instances.get(hostElementId);
    if (!inst) return { text: '', startLine: 1, startColumn: 1 };
    const state = inst.view.state;
    const sel = state.selection.main;
    // Spec 042: the column the selection starts at on its first line, so an error on that line maps back to the document.
    const startColumn = sel.from - state.doc.lineAt(sel.from).from + 1;
    return { text: state.sliceDoc(sel.from, sel.to), startLine: selectionStartLine(state.doc, sel), startColumn };
}

/** Replace the entire document (spec 043: of query tab `tabId` when it is not the shown one, without a text report). */
export function setText(hostElementId, newText, tabId) {
    const inst = _instances.get(hostElementId);
    if (!inst) return;
    const stored = storedTab(inst, tabId, true);
    if (stored) {
        stored.state = replaceDoc(stored.state, newText);
        return;
    }
    inst.view.dispatch({
        changes: { from: 0, to: inst.view.state.doc.length, insert: newText ?? '' },
    });
}

/**
 * Review WEB-10 — replace the whole document only if it is still exactly `expected` (what a Format sent), as one undoable
 * change. The check and the change happen in the same turn, so no keystroke can land between them. Returns false, changing
 * nothing, when the document moved on.
 */
export function replaceIfUnchanged(view, expected, newText) {
    if (!view || view.state.doc.toString() !== (expected ?? '')) return false;
    view.dispatch({ changes: { from: 0, to: view.state.doc.length, insert: newText ?? '' } });
    return true;
}

export function replaceTextIfUnchanged(hostElementId, expected, newText, tabId) {
    const inst = _instances.get(hostElementId);
    if (!inst) return false;
    const stored = storedTab(inst, tabId);
    if (stored === undefined) return false;   // never shown here: nothing of it was formatted
    if (stored) {
        // Spec 043: a Format reply for a tab the user left meanwhile goes into that tab's stored state.
        const next = replaceDocIfUnchanged(stored.state, expected, newText);
        if (!next) return false;
        stored.state = next;
        return true;
    }
    return replaceIfUnchanged(inst.view, expected, newText);
}

/**
 * Spec 025 (M3 bridge closure) US4 — insert text at the current caret. Used by
 * SchemaTreeComponent's click-to-insert: clicking [dbo].[Customer] drops that
 * literal at the caret as-typed; CodeMirror's standard Ctrl+Z undoes it.
 */
/**
 * A name from the schema explorer, ready to insert between `before` and `after` (the characters either side of the
 * caret): a space is added where the name would otherwise run into a word, a closing bracket or quote, or a
 * parenthesis - `[dbo].[Customers]SELECT` cannot happen.
 */
export function padName(before, after, text) {
    const joins = /[\w\]")@#$]/;
    const lead = before && joins.test(before) ? ' ' : '';
    const trail = after && /[\w\["(@#]/.test(after) ? ' ' : '';
    return lead + text + trail;
}

/**
 * The explorer's Insert name (double-click or the context menu): replaces the selection, or goes in at the caret,
 * padded by padName. One transaction, so a single Ctrl+Z takes it out again.
 */
export function insertNameAtCaret(hostElementId, text) {
    const inst = _instances.get(hostElementId);
    if (!inst || typeof text !== 'string' || text.length === 0) return;
    const { from, to } = inst.view.state.selection.main;
    const doc = inst.view.state.doc;
    const before = from > 0 ? doc.sliceString(from - 1, from) : '';
    const after = to < doc.length ? doc.sliceString(to, to + 1) : '';
    const insert = padName(before, after, text);
    inst.view.dispatch({
        changes: { from, to, insert },
        selection: { anchor: from + insert.length },
        userEvent: 'input',
    });
    inst.view.focus();
}

/** Close the completion list, if open: Execute closes it, so it does not sit over the editor while the query runs. */
export function closeCompletion(hostElementId) {
    const inst = _instances.get(hostElementId);
    if (inst && inst.cm && inst.cm.autocomplete && inst.cm.autocomplete.closeCompletion) inst.cm.autocomplete.closeCompletion(inst.view);
}

export function insertAtCaret(hostElementId, text) {
    const inst = _instances.get(hostElementId);
    if (!inst || typeof text !== 'string' || text.length === 0) return;
    const head = inst.view.state.selection.main.head;
    inst.view.dispatch({
        changes: { from: head, insert: text },
        selection: { anchor: head + text.length },
    });
    inst.view.focus();
}

// ── Spec 027 (M5 offline closure) T010/T011 — snippet expansion + surround-with ──────────
//
// Snippet bodies are authored in the ENGINE-NATIVE placeholder syntax (so a web-authored
// .akmlsnippet ALSO expands on the SSMS/WPF surface — FR-006): a tab-stop is `$Name$`
// (must start with a letter/underscore), the final caret is `$CURSOR$`, and the selection
// slot for surround-with is `$SELECTEDTEXT$`. These helpers translate that into CodeMirror 6's
// `${...}` snippet template at expand time, falling back to a literal insertion if CM6's
// snippet() is unavailable (the function is feature-detected — it cannot be verified headless).

// Engine-native named token, e.g. `$table$` / `$CURSOR$` (AkmlSql.Engine PlaceholderParser:
// regex \$([A-Za-z_]\w*)\$). Numbered `$1`/`$2` tokens (the shared SnippetProvider dialect)
// are handled separately — both normalise to CM6 `${...}` fields so the one expander serves
// every in-repo snippet source.
const SNIPPET_NAMED = /\$([A-Za-z_]\w*)\$/g;
const SNIPPET_NUMBERED = /\$(\d+)/g;

/**
 * Translate a snippet body into a CodeMirror 6 snippet template.
 * `$Name$`   -> `${Name}`  (CM6 tab-stop; repeated names link, matching the engine)
 * `$CURSOR$` -> `${}`       (CM6 final cursor stop)
 * `$1`,`$2`  -> `${1}`,`${2}` (the SnippetProvider numbered-stop dialect)
 * Escapes any pre-existing `${` so literal text is not mis-read as a CM6 field.
 */
function toCmTemplate(body) {
    return String(body)
        .replace(/\$\{/g, '\\${')                          // protect literal ${ in user text
        .replace(SNIPPET_NAMED, (_m, name) =>
            name.toUpperCase() === 'CURSOR' ? '${}' : '${' + name + '}')
        .replace(SNIPPET_NUMBERED, (_m, n) => '${' + n + '}');
}

/**
 * Strip snippet tokens to plain text for the no-CM6-snippet fallback. Named stops become
 * their name, numbered stops vanish, `$CURSOR$` is removed. The caret lands at end of the
 * inserted text (an exact mid-body caret is not worth reconstructing for the rare fallback).
 */
function toLiteral(body) {
    return String(body)
        .replace(SNIPPET_NAMED, (_m, name) => name.toUpperCase() === 'CURSOR' ? '' : name)
        .replace(SNIPPET_NUMBERED, '');
}

/**
 * Inserts a completion that carries square brackets without doubling one the user already typed.
 *
 * CM's word match (`/[@#\w]+/`) stops at "[" and at spaces, so for `[Order D|` the replaced range
 * is only "D". Replacing just that range with "[Order Details]" gives "[Order [Order Details]".
 * So when the insert BEGINS with "[", scan back from the match start for the "[" that opens the name
 * being typed, and widen the replacement to start there. Only letters, digits, spaces and _ - # @ $
 * may lie between that "[" and the partial (and at most 128 of them): anything else — a "]", a
 * comma, an operator, a quote, a comment — means the "[" belongs to something else and is left alone.
 *
 * Symmetrically, a "]" sitting immediately after the caret (auto-closed brackets) is consumed when
 * the insert ENDS with "]", so the result is never "[Order Details]]".
 *
 * Exported for tests; pure except for the dispatch.
 */
export function bracketedRange(doc, insertText, from, to) {
    let start = from;
    let end = to;

    if (insertText.startsWith('[')) {
        // Only the "[" that opens the name being typed: everything between it and the partial must
        // be what a bracketed name is made of, so a "[" in a string, a comment or another name is
        // never swallowed. Same rule as the SSMS shell's BracketedRange.
        for (let k = from - 1; k >= 0 && from - k <= 128; k--) {
            const c = doc.sliceString(k, k + 1);
            if (c === '[') { start = k; break; }
            if (!/[\p{L}\p{Nd} _\-#@$]/u.test(c)) break;
        }
    }

    if (insertText.endsWith(']') && end < doc.length && doc.sliceString(end, end + 1) === ']') {
        end += 1;
    }

    return { from: start, to: end };
}

function applyBracketed(view, insertText, from, to) {
    const range = bracketedRange(view.state.doc, insertText, from, to);
    view.dispatch({
        changes: { from: range.from, to: range.to, insert: insertText },
        selection: { anchor: range.from + insertText.length },
        // Keep it a single undo step and let CM treat it as a completion pick.
        userEvent: 'input.complete',
    });
}

function applySnippetBody(cm, view, body, from, to) {
    if (typeof body !== 'string' || body.length === 0) return;
    const snippetFn = cm.autocomplete && cm.autocomplete.snippet;
    if (typeof snippetFn === 'function') {
        try {
            // CM6: snippet(template) -> (view, completion, from, to) => void
            snippetFn(toCmTemplate(body))(view, null, from, to);
            view.focus();
            return;
        } catch { /* fall through to literal */ }
    }
    const text = toLiteral(body);
    view.dispatch({
        changes: { from, to, insert: text },
        selection: { anchor: from + text.length },
    });
    view.focus();
}

/**
 * Spec 027 T010 — expand a snippet body at the caret (replacing any current selection),
 * driving CM6 tab-stops. Body is engine-native (`$Name$` / `$CURSOR$`).
 */
export function expandSnippet(hostElementId, body) {
    const inst = _instances.get(hostElementId);
    if (!inst) return;
    const sel = inst.view.state.selection.main;
    applySnippetBody(inst.cm, inst.view, body, sel.from, sel.to);
}

/**
 * Spec 027 T010 — surround the current selection: the snippet body's `$SELECTEDTEXT$` token
 * is replaced by the selected text, then the body expands as a normal snippet at the
 * selection range. With no selection, `$SELECTEDTEXT$` resolves to empty (caret lands there).
 */
export function surroundSelection(hostElementId, body) {
    const inst = _instances.get(hostElementId);
    if (!inst || typeof body !== 'string') return;
    const sel = inst.view.state.selection.main;
    const selected = inst.view.state.sliceDoc(sel.from, sel.to);
    // Substitute the selection slot first (case-insensitive, engine-native token), then let
    // the standard snippet path handle any remaining $Name$ / $CURSOR$ tab-stops.
    const resolved = body.replace(/\$SELECTEDTEXT\$/gi, () => selected);
    applySnippetBody(inst.cm, inst.view, resolved, sel.from, sel.to);
}

/** Select the given (1-based) line and scroll it into view. */
export function gotoLine(hostElementId, lineNumber1Based) {
    const inst = _instances.get(hostElementId);
    if (!inst) return;
    const doc = inst.view.state.doc;
    const line = Math.max(1, Math.min(lineNumber1Based, doc.lines));
    const lineInfo = doc.line(line);
    // Select the whole line range so the user sees the offending span highlighted,
    // not just an invisible caret at column 0.
    inst.view.dispatch({
        selection: { anchor: lineInfo.from, head: lineInfo.to },
        effects: inst.cm.view.EditorView.scrollIntoView(lineInfo.from, { y: 'center' }),
    });
    inst.view.focus();
}

/**
 * Spec 042 (FR-014): put the caret at a (1-based) line and column and scroll it into view. A PostgreSQL error knows its
 * column, so the caret lands on the token the server named instead of selecting the whole line. A line or column past
 * the document is clamped to its end.
 */
export function gotoPosition(hostElementId, lineNumber1Based, column1Based) {
    const inst = _instances.get(hostElementId);
    if (!inst) return;
    const doc = inst.view.state.doc;
    const line = Math.max(1, Math.min(lineNumber1Based, doc.lines));
    const lineInfo = doc.line(line);
    const offset = Math.max(0, Math.min((column1Based || 1) - 1, lineInfo.length));
    inst.view.dispatch({
        selection: { anchor: lineInfo.from + offset },
        effects: inst.cm.view.EditorView.scrollIntoView(lineInfo.from + offset, { y: 'center' }),
    });
    inst.view.focus();
}

// ── Spec 041 (R39) — findings in the editor ──────────────────────────────────────────────────────
//
// Analyser findings (FR-087) and the line of an execution error (FR-023) are shown through CodeMirror's
// lint state: lintGutter() is installed at create time; these exports feed it. lintKeymap is NOT
// installed — it binds F8, which the editor page uses for the Schema panel.

const LINT_SEVERITY = ['hint', 'info', 'warning', 'error'];

/**
 * Pure: translate line-based findings into CodeMirror diagnostics, clamped to the document.
 * @param {{lines:number, length:number, line:(n:number)=>{from:number,to:number}}} doc  a CM Text
 * @param {Array<{line:number, startColumn?:number|null, endColumn?:number|null, severity:number, message:string, source?:string}>} diags
 *        1-based line and columns; severity 0..3 = hint/info/warning/error.
 * @returns {Array<{from:number, to:number, severity:string, message:string, source:string}>}
 */
export function toLintDiagnostics(doc, diags) {
    if (!Array.isArray(diags) || !doc) return [];
    const out = [];
    for (const d of diags) {
        if (!d || typeof d.line !== 'number' || d.line < 1 || d.line > doc.lines) continue;
        const line = doc.line(d.line);
        let from = line.from;
        let to = line.to;
        if (typeof d.startColumn === 'number' && d.startColumn >= 1) {
            from = Math.min(line.to, line.from + d.startColumn - 1);
        }
        if (typeof d.endColumn === 'number' && d.endColumn >= 1) {
            to = Math.min(line.to, line.from + d.endColumn - 1);
        }
        // An end at or before the start (or a missing end) underlines the whole line.
        if (to <= from) {
            from = line.from;
            to = line.to;
        }
        out.push({
            from,
            to,
            severity: LINT_SEVERITY[d.severity] ?? 'info',
            message: String(d.message ?? ''),
            source: d.source || 'AKML SQL',
        });
    }
    return out;
}

/** Show `diags` (see toLintDiagnostics) as underlines + gutter markers, replacing any shown before. */
export function setDiagnostics(hostElementId, diags, tabId) {
    const inst = _instances.get(hostElementId);
    if (!inst || !inst.cm.lint || typeof inst.cm.lint.setDiagnostics !== 'function') return;
    const stored = storedTab(inst, tabId);
    if (stored === undefined) return;   // never shown here: the page puts its findings back when it is first shown
    if (stored) {
        stored.state = withDiagnostics(inst.cm, stored.state, diags);   // spec 043: findings for a tab not shown
        return;
    }
    const mapped = toLintDiagnostics(inst.view.state.doc, diags);
    inst.view.dispatch(inst.cm.lint.setDiagnostics(inst.view.state, mapped));
}

/** Remove every finding shown in the editor (spec 043: of query tab `tabId` when it is not the shown one). */
export function clearDiagnostics(hostElementId, tabId) {
    setDiagnostics(hostElementId, [], tabId);
}

/**
 * Spec 041 (T200) — select the next (forward) or previous finding, wrapping, as the palette's Next/Previous
 * problem. False when the editor shows none.
 */
export function goToProblem(hostElementId, forward) {
    const inst = _instances.get(hostElementId);
    if (!inst || !inst.cm.lint) return false;
    if (typeof inst.cm.lint.diagnosticCount === 'function' && inst.cm.lint.diagnosticCount(inst.view.state) === 0) return false;
    const command = forward ? inst.cm.lint.nextDiagnostic : inst.cm.lint.previousDiagnostic;
    if (typeof command !== 'function') return false;
    inst.view.focus();
    return command(inst.view) === true;
}

/**
 * Spec 027 T025 (US4) — append text at the end of a 1-based line. Used by the suppression
 * "this line" action to insert ` -- noqa: RULEID` at the finding's line end (matching the
 * WPF FixAction). Single dispatch ⇒ one undoable edit. Returns the resulting full text via
 * the caller's getText if needed; here we just mutate.
 */
export function insertAtLineEnd(hostElementId, lineNumber1Based, text) {
    const inst = _instances.get(hostElementId);
    if (!inst || typeof text !== 'string' || text.length === 0) return;
    const doc = inst.view.state.doc;
    const line = Math.max(1, Math.min(lineNumber1Based, doc.lines));
    const lineInfo = doc.line(line);
    inst.view.dispatch({ changes: { from: lineInfo.to, insert: text } });
    inst.view.focus();
}

/** Replace the selection range with the cursor at offset = anchor. */
export function setSelection(hostElementId, anchor, head) {
    const inst = _instances.get(hostElementId);
    if (!inst) return;
    inst.view.dispatch({ selection: { anchor, head } });
}

/** Dispose the editor and free the DotNetObjectReference. */
export function dispose(hostElementId) {
    const inst = _instances.get(hostElementId);
    if (!inst) return;
    // Spec 028 (M6) US5: cancel any pending ghost-text debounce so it can't fire requestGhost
    // on a destroyed view / disposed DotNetObjectReference, and supersede any in-flight response.
    if (inst.ghost) {
        if (inst.ghost.timer) { clearTimeout(inst.ghost.timer); inst.ghost.timer = null; }
        inst.ghost.reqId++;
    }
    const host = document.getElementById(hostElementId);
    if (host && host._akmlPeek) { host._akmlPeek.remove(); host._akmlPeek = null; }
    inst.disposeLintKeyboard?.();
    inst.view.destroy();
    if (inst.dotNetRef && typeof inst.dotNetRef.dispose === 'function') {
        inst.dotNetRef.dispose();
    }
    _instances.delete(hostElementId);
}
