// Spec 046 (US6, FR-024) — the chat log follows a new answer only when the reader is already at
// the bottom. Run with:
//   node --test "tests/AkmlSql.Web.Tests/js/*.test.mjs"
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { isAtBottom, scrollToBottom } from '../../../src/AkmlSql.Web/wwwroot/js/akml-ui.js';

function log(scrollTop, scrollHeight = 1000, clientHeight = 300) {
    return { scrollTop, scrollHeight, clientHeight };
}

test('isAtBottom: at the end, or within the 8 px threshold', () => {
    assert.equal(isAtBottom(log(700)), true);
    assert.equal(isAtBottom(log(692)), true);
    assert.equal(isAtBottom(log(691)), false);
});

test('isAtBottom: a log with nothing to scroll is at the bottom', () => {
    assert.equal(isAtBottom(log(0, 200, 300)), true);
});

test('isAtBottom: honours a custom threshold and tolerates a missing element', () => {
    assert.equal(isAtBottom(log(650), 50), true);
    assert.equal(isAtBottom(log(649), 50), false);
    assert.equal(isAtBottom(null), true);
});

test('scrollToBottom: moves to the end and tolerates a missing element', () => {
    const el = log(0);
    scrollToBottom(el);
    assert.equal(el.scrollTop, 1000);
    assert.doesNotThrow(() => scrollToBottom(null));
});
