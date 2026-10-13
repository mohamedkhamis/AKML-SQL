// Unit tests for resolveKey in akml-workspace.js (spec 041 T028, research R37, FR-049). Run with:
//   node --test "tests/AkmlSql.Web.Tests/js/*.test.mjs"
//
// The editor page claims SSMS's keys (F5 execute, Ctrl+F5 parse, Ctrl+R results, F8 schema, F6 panes,
// the Ctrl+K chords) in a document-level capture listener so it can call preventDefault BEFORE the
// browser reloads or CodeMirror opens Find. resolveKey is the pure decision: claim this key or not,
// and which command to run. The binding table is the one WorkspaceKeyMap.cs sends from .NET.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { resolveKey } from '../../../src/AkmlSql.Web/wwwroot/js/akml-workspace.js';

const bindings = [
    { code: 'F5', ctrl: false, shift: false, alt: false, commandId: 'editor:execute', chordPrefix: null },
    { code: 'F5', ctrl: true, shift: false, alt: false, commandId: 'editor:parse', chordPrefix: null },
    { code: 'KeyR', ctrl: true, shift: false, alt: false, commandId: 'view:toggle-results', chordPrefix: null },
    { code: 'F8', ctrl: false, shift: false, alt: false, commandId: 'view:toggle-schema', chordPrefix: null },
    { code: 'F6', ctrl: false, shift: false, alt: false, commandId: 'view:cycle-panes', chordPrefix: null },
    { code: 'F6', ctrl: false, shift: true, alt: false, commandId: 'view:cycle-panes-back', chordPrefix: null },
    { code: 'KeyS', ctrl: true, shift: false, alt: false, commandId: 'editor:save', chordPrefix: null },
    { code: 'KeyS', ctrl: true, shift: true, alt: false, commandId: 'editor:saveas', chordPrefix: null },
    { code: 'KeyN', ctrl: true, shift: false, alt: true, commandId: 'editor:new', chordPrefix: null },
    { code: 'KeyO', ctrl: true, shift: false, alt: true, commandId: 'editor:open', chordPrefix: null },
    { code: 'KeyF', ctrl: true, shift: false, alt: false, commandId: 'editor:format', chordPrefix: 'Ctrl+KeyK' },
    { code: 'KeyL', ctrl: true, shift: false, alt: false, commandId: 'editor:analyse', chordPrefix: 'Ctrl+KeyK' },
    { code: 'KeyS', ctrl: true, shift: false, alt: false, commandId: 'editor:surround', chordPrefix: 'Ctrl+KeyK' },
    { code: 'KeyA', ctrl: true, shift: false, alt: false, commandId: 'editor:saveas', chordPrefix: 'Ctrl+KeyK' },
];

/** A keydown as the listener describes it to resolveKey. */
function key(code, mods = {}, extra = {}) {
    const k = extra.key ?? (code.startsWith('Key') ? code.slice(3).toLowerCase() : code);
    return {
        code,
        key: k,
        ctrlKey: !!mods.ctrl,
        shiftKey: !!mods.shift,
        altKey: !!mods.alt,
        metaKey: !!mods.meta,
        repeat: !!extra.repeat,
        altGraph: !!extra.altGraph,
    };
}

const fresh = () => ({ chord: null, chordExpires: 0 });

test('each direct binding resolves to its command and is claimed', () => {
    const cases = [
        [key('F5'), 'editor:execute'],
        [key('F5', { ctrl: true }), 'editor:parse'],
        [key('KeyR', { ctrl: true }), 'view:toggle-results'],
        [key('F8'), 'view:toggle-schema'],
        [key('F6'), 'view:cycle-panes'],
        [key('F6', { shift: true }), 'view:cycle-panes-back'],
        [key('KeyS', { ctrl: true }), 'editor:save'],
        [key('KeyS', { ctrl: true, shift: true }, { key: 'S' }), 'editor:saveas'],
        [key('KeyN', { ctrl: true, alt: true }), 'editor:new'],
        [key('KeyO', { ctrl: true, alt: true }), 'editor:open'],
    ];
    for (const [evt, id] of cases) {
        const r = resolveKey(bindings, fresh(), evt, 1000);
        assert.equal(r.claim, true, `${id} is claimed`);
        assert.equal(r.commandId, id);
    }
});

test('matching uses e.code, so the shifted letter still matches (Ctrl+Shift+S has e.key "S")', () => {
    const r = resolveKey(bindings, fresh(), key('KeyS', { ctrl: true, shift: true }, { key: 'S' }), 0);
    assert.equal(r.commandId, 'editor:saveas');
});

test('Cmd on macOS counts as Ctrl', () => {
    const r = resolveKey(bindings, fresh(), key('KeyR', { meta: true }), 0);
    assert.equal(r.commandId, 'view:toggle-results');
});

test('modifiers must match exactly', () => {
    assert.equal(resolveKey(bindings, fresh(), key('F5', { shift: true }), 0).claim, false);
    assert.equal(resolveKey(bindings, fresh(), key('KeyR', { ctrl: true, shift: true }), 0).claim, false);
    assert.equal(resolveKey(bindings, fresh(), key('KeyR'), 0).claim, false);
});

test('an unbound key is left alone', () => {
    const r = resolveKey(bindings, fresh(), key('KeyA', { ctrl: true }), 0);
    assert.equal(r.claim, false);
    assert.equal(r.commandId, null);
});

test('a lone Ctrl+F is not claimed, so CodeMirror Find still works', () => {
    const r = resolveKey(bindings, fresh(), key('KeyF', { ctrl: true }), 0);
    assert.equal(r.claim, false);
});

test('Ctrl+K then Ctrl+A offers Save as without taking plain Select all', () => {
    const start = resolveKey(bindings, fresh(), key('KeyK', { ctrl: true }), 1000);
    const done = resolveKey(bindings, start.state, key('KeyA', { ctrl: true }), 1200);
    assert.equal(done.claim, true);
    assert.equal(done.commandId, 'editor:saveas');
    assert.equal(resolveKey(bindings, fresh(), key('KeyA', { ctrl: true }), 1200).claim, false);
});

test('Ctrl+K starts a chord (claimed, no command); Ctrl+F within 1.5 s completes it', () => {
    const start = resolveKey(bindings, fresh(), key('KeyK', { ctrl: true }), 1000);
    assert.equal(start.claim, true);
    assert.equal(start.commandId, null);
    assert.equal(start.state.chord, 'Ctrl+KeyK');

    const done = resolveKey(bindings, start.state, key('KeyF', { ctrl: true }), 2000);
    assert.equal(done.claim, true, 'the second stroke never reaches CodeMirror Find');
    assert.equal(done.commandId, 'editor:format');
    assert.equal(done.state.chord, null);
});

test('the chord second strokes: Ctrl+L analyses, Ctrl+S surrounds (not save)', () => {
    const start = resolveKey(bindings, fresh(), key('KeyK', { ctrl: true }), 0);
    assert.equal(resolveKey(bindings, start.state, key('KeyL', { ctrl: true }), 100).commandId, 'editor:analyse');
    assert.equal(resolveKey(bindings, start.state, key('KeyS', { ctrl: true }), 100).commandId, 'editor:surround');
});

test('after 1.5 s the chord has expired and Ctrl+F is a plain key again', () => {
    const start = resolveKey(bindings, fresh(), key('KeyK', { ctrl: true }), 1000);
    const late = resolveKey(bindings, start.state, key('KeyF', { ctrl: true }), 2600);
    assert.equal(late.claim, false);
    assert.equal(late.commandId, null);
    assert.equal(late.state.chord, null);
});

test('a modifier keydown during a chord keeps the chord alive', () => {
    const start = resolveKey(bindings, fresh(), key('KeyK', { ctrl: true }), 0);
    const mod = resolveKey(bindings, start.state, { ...key('ControlLeft', { ctrl: true }), key: 'Control' }, 100);
    assert.equal(mod.claim, false);
    assert.equal(mod.state.chord, 'Ctrl+KeyK');
    assert.equal(resolveKey(bindings, mod.state, key('KeyF', { ctrl: true }), 200).commandId, 'editor:format');
});

test('an unknown second stroke cancels the chord and falls through to the direct bindings', () => {
    const start = resolveKey(bindings, fresh(), key('KeyK', { ctrl: true }), 0);
    const other = resolveKey(bindings, start.state, key('F5'), 100);
    assert.equal(other.commandId, 'editor:execute');
    assert.equal(other.state.chord, null);
});

test('auto-repeat is claimed (held F5 must not reload) but runs nothing', () => {
    const r = resolveKey(bindings, fresh(), key('F5', {}, { repeat: true }), 0);
    assert.equal(r.claim, true);
    assert.equal(r.commandId, null);
});

test('nothing resolves while a modal dialog is open', () => {
    const state = { ...fresh(), modalOpen: true };
    const r = resolveKey(bindings, state, key('F5'), 0);
    assert.equal(r.claim, false);
    assert.equal(r.commandId, null);
});

test('AltGr: Ctrl+Alt+O that types a character (Polish ó) is never claimed', () => {
    const typed = resolveKey(bindings, fresh(), key('KeyO', { ctrl: true, alt: true }, { key: 'ó' }), 0);
    assert.equal(typed.claim, false);
    const altGraph = resolveKey(bindings, fresh(), key('KeyN', { ctrl: true, alt: true }, { altGraph: true }), 0);
    assert.equal(altGraph.claim, false);
    // The plain letter on a US layout is still the New query command.
    assert.equal(resolveKey(bindings, fresh(), key('KeyN', { ctrl: true, alt: true }), 0).commandId, 'editor:new');
});

test('resolveKey never mutates the state it is given', () => {
    const state = fresh();
    resolveKey(bindings, state, key('KeyK', { ctrl: true }), 0);
    assert.deepEqual(state, fresh());
});
