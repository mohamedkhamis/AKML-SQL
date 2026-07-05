// Vendor entry point. Re-exports each CodeMirror 6 package the web editor uses as a namespace,
// matching the { state, view, commands, language, langSql, autocomplete, search, lint, highlight }
// shape that akml-editor.js's loadCm() consumes. esbuild bundles this into a single ESM file with a
// SINGLE shared @codemirror/state instance — CodeMirror requires exactly one copy of state/view, so
// this must stay one bundle (never per-package files, which would duplicate state and break facets).
export * as state from '@codemirror/state';
export * as view from '@codemirror/view';
export * as commands from '@codemirror/commands';
export * as language from '@codemirror/language';
export * as langSql from '@codemirror/lang-sql';
export * as autocomplete from '@codemirror/autocomplete';
export * as search from '@codemirror/search';
export * as lint from '@codemirror/lint';
export * as highlight from '@lezer/highlight';
