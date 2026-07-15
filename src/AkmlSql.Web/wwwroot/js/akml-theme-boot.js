// Boot-time theme application. Extracted from an inline <script> in index.html so the page needs
// no 'unsafe-inline'/hash in the CSP script-src (strict-CSP / on-prem support). Applies 'system'
// immediately so the page picks the right mode before Blazor boots; the Blazor ThemeService
// re-applies later with the user's stored preference once IndexedDB is reachable.
import { apply } from './akml-theme.js';
apply('system');
