// UI test 2026-10-18 (M1, M6): a name from the schema explorer never runs into the text around it, and the completion
// list does not open on the `;` that ends a statement. Run with:
//   node --test "tests/AkmlSql.Web.Tests/js/*.test.mjs"
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { padName, endsStatement } from '../../../src/AkmlSql.Web/wwwroot/js/akml-editor.js';

test('a name typed against a word or a closing bracket gets a space before it', () => {
    assert.equal(padName('M', '', '[dbo].[Customers]'), ' [dbo].[Customers]');
    assert.equal(padName(']', '', 'x'), ' x');
    assert.equal(padName('"', '', 'x'), ' x');
});

test('a name in front of a word gets a space after it', () => {
    assert.equal(padName('', 'S', '[dbo].[Customers]'), '[dbo].[Customers] ');
    assert.equal(padName(' ', '[', 'x'), 'x ');
});

test('after a space, a comma, an opening parenthesis or a dot it goes in as it is', () => {
    assert.equal(padName(' ', ' ', 'x'), 'x');
    assert.equal(padName(',', '\n', 'x'), 'x');
    assert.equal(padName('(', ')', 'x'), 'x');
    assert.equal(padName('', '', 'x'), 'x');
});

test('a semicolon before the caret ends the statement', () => {
    assert.equal(endsStatement('SELECT 1;'), true);
    assert.equal(endsStatement('SELECT 1; '), false);
    assert.equal(endsStatement('SELECT '), false);
    assert.equal(endsStatement(undefined), false);
});
