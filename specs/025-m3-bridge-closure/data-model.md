# Data Model: M3 — WebSocket Transport & Local-Agent Bridge Closure

**Branch**: `025-m3-bridge-closure` | **Date**: 2026-05-27 | **Spec**: [spec.md](./spec.md)

This closure spec introduces no new persistence — every persistent record M3 needs is already shipped by spec 021 (`EngineConnection` in `IConnectionStore`, `PersistedToken` in `IPairingTokenVault`, `SchemaSnapshot` in `ISchemaCacheStore`, `BearerTokenStore.json` on the engine side). The entities below are *conceptual*: in-memory state, document-row contracts, and a test-fixture state machine. They exist so tasks.md can name them without ambiguity.

---

## E1 — BackoffSchedule

**Owner**: `EngineBridge` (private struct/class inside `src/AkmlSql.Web/Services/IEngineBridge.cs`).

**Purpose**: Generate the sequence of intervals between reconnect attempts per Research Decision 2.

**Fields**:

| Field | Type | Meaning |
|-------|------|---------|
| `InitialDelay` | `TimeSpan` | First retry interval. **Constant**: 500 ms. |
| `Multiplier` | `double` | Doubling factor between attempts. **Constant**: 2.0. |
| `MaxDelay` | `TimeSpan` | Cap after which doubling stops. **Constant**: 30 s. |
| `JitterRange` | `TimeSpan` | ± window applied uniformly to each interval. **Constant**: 100 ms. |
| `AttemptNumber` | `int` | 1-based counter of retries since the last `Open` state. Reset to 0 on every transition to `Open`. |

**Method**: `TimeSpan NextDelay()` → returns `min(InitialDelay × Multiplier^(AttemptNumber-1), MaxDelay) + RandomBetween(-JitterRange, JitterRange)`; increments `AttemptNumber`.

**Validation rules**:

- `InitialDelay`, `MaxDelay` MUST be > 0.
- `Multiplier` MUST be ≥ 1.0 (otherwise back-off doesn't grow).
- `JitterRange` MUST be ≥ 0 and ≤ `InitialDelay / 2` (so jitter cannot push a delay negative).

**State transitions** (driven by `EngineBridge.State`):

- `Open` → `Reconnecting`: reset `AttemptNumber = 0`; call `NextDelay()` for the first wait.
- `Reconnecting` → `Connecting` (retry in flight): `AttemptNumber` carried over; no schedule mutation.
- `Connecting` → `Open` (retry succeeded): reset `AttemptNumber = 0`; schedule is dormant until next disconnect.
- `Connecting` → `Failed` (handshake returned `PinRequired`): schedule discarded; loop exits.

**Test-visibility**: exposed via an `internal` constructor that accepts a `Func<TimeSpan, TimeSpan, TimeSpan>` for the jitter source so `ReconnectLoopTests` can assert deterministic intervals.

---

## E2 — SchemaTreeNode

**Owner**: `SchemaTreeComponent.razor` (in-memory only; never persists).

**Purpose**: One rendered node in the Database → Schema → Object-Kind → Object → Column tree per Research Decision 3.

**Fields**:

| Field | Type | Meaning |
|-------|------|---------|
| `Kind` | `enum SchemaNodeKind { Database, Schema, ObjectKind, Object, Column }` | Determines icon + child shape. |
| `Name` | `string` | Display label. For `Object`: the unqualified name; for `Schema`: the schema name; for `ObjectKind`: a localised "Tables"/"Views"/"Stored Procedures"/"Functions" header. |
| `Path` | `string` | Slash-delimited stable key: `<database>/<schema>/<object-kind>/<object>[/<column>]`. Used for expansion-state preservation across snapshot refreshes. |
| `QualifiedName` | `string?` | Bracket-quoted identifier for click-to-insert. Format `[schema].[name]` for `Object`; null for non-`Object` nodes. |
| `Children` | `IReadOnlyList<SchemaTreeNode>` | Lazy-built on first expand. For nodes whose child count exceeds the virtualisation threshold (200), rendered via Blazor `<Virtualize>`. |
| `IsLazy` | `bool` | True when `Children` is unmaterialised (the node was rendered collapsed). False once expansion materialised the list. |

**Construction rule**:

- A `SchemaTreeNode` MUST be built only from a `SchemaSnapshot` record already in `ISchemaCacheStore`. The tree never round-trips to the bridge.
- `Children` MUST preserve the `[schema].[name]` collation order from the snapshot — no in-component re-sort.

**State transitions** (per click in the editor sidebar):

- collapsed → expanded: `IsLazy` flips to false; `Children` materialised from the snapshot.
- expanded → collapsed: visible-only collapse; `Children` retained so re-expand is free.
- snapshot refresh fires while expanded: `HashSet<string>` of expanded paths preserved across the rebuild (FR-021).

**Validation rules**:

- `Kind == Object` ⇒ `QualifiedName` MUST be non-null.
- `Kind == Column` ⇒ no `Children` allowed.
- `Path` MUST be unique within a tree (used as a dictionary key for expansion state).

---

## E3 — ThreatModelEntry

**Owner**: `doc/m3-security.md` (one row of the markdown threat-model table per Research Decision 5 + FR-007).

**Purpose**: Structured row format the threat-model document follows so a reviewer can scan it.

**Fields**:

| Column | Meaning |
|--------|---------|
| **Threat** | One-line description of the adversary capability. |
| **Mitigation** | What the system does to reduce the threat (code refusal, ACL, single-use PIN, TTL, etc.). |
| **Residual risk** | What the operator still has to accept after the mitigation. Marked `none` for hard-refusal items. |

**Required rows** (FR-007):

The eight rows verbatim from PRD §8 Threat Model:
1. "Anyone on the LAN connects to the WebSocket" → Pairing-token bearer auth required.
2. "Eavesdropper captures the token over plaintext WebSocket" → (now closed by FR-001 + this spec) → "TLS via netsh-bound cert (HTTPS prefix); plaintext LAN refused at construction."
3. "Replay attack with captured token" → 90-day TTL + manual rotation via Remove-and-re-pair.
4. "Brute force the 6-digit PIN" → Single-use; 5-min expiry; rate-limit 3 wrong/15 min.
5. "Token file stolen from disk" → ACL to engine user; documented as "physical access = total access."
6. "Man-in-the-middle on first pair" → Now mitigated by FR-001 (TLS-wrapped pair) plus the fingerprint diagnostic (FR-005); the user-facing fingerprint dialog remains a deferred follow-up.

Plus the two added rows FR-007 requires:

7. "Plaintext-on-LAN attempted via hand-edited config.json" → Refused at `WebSocketTransport` construction with message naming `TlsCertPath` and FR-013a.
8. "Cert regeneration on installer re-run silently swaps fingerprint" → Diagnostic warning logged when `EngineConnection.TlsFingerprint` changes; user-facing modal deferred (Out of Scope #1).

---

## E4 — QuickstartStep

**Owner**: `doc/WEB/quickstart-m3.md` (per FR-008 + FR-010).

**Purpose**: One numbered step in the pair-from-second-machine walkthrough.

**Fields**:

| Field | Meaning |
|-------|---------|
| **Step number** | 1-based; matches the existing `quickstart-m2.md` / `quickstart-m4.md` numbering style. |
| **Heading** | Short imperative ("Install the web edition with LAN mode", "Accept the firewall prompt", "Copy the PIN", …). |
| **Body** | The literal commands / clicks / screenshots needed to perform the step. |
| **Verification** | One sentence describing how the reader confirms the step worked. |

**Required steps** (covers FR-008 end-to-end walkthrough):

1. Run `AKMLSQLSetup.exe /WEB_EXPOSURE=LAN /WEB_PORT=47291` on Machine A. → Verify the install-summary file contains the PIN and the TLS thumbprint.
2. Accept the Windows Firewall prompt (or confirm the installer-created inbound rule via the firewall guidance in spec 021 T089). → Verify with `netsh advfirewall firewall show rule name="AKML SQL Web Engine"`.
3. Open the web edition in a browser on Machine B (use Machine A's hostname or LAN IP). → Verify the editor page loads with `BridgeState.Disconnected`.
4. Click **Add Connection**; fill in Machine A's IP + port + the PIN from the install summary. → Verify the bridge transitions through `Connecting` → `Open`.
5. Type a `SELECT` into the editor; observe IntelliSense from Machine A's live schema. → Verify a column-aware completion appears.

Plus a "Troubleshooting" appendix covering the most common failure modes (firewall blocked, wrong PIN, wrong port, stale netsh binding).

---

## E5 — BridgeE2EFixtureState

**Owner**: `EngineLaunchFixture` (private state in `tests/AkmlSql.Web.E2E.Tests/Harness/EngineLaunchFixture.cs`).

**Purpose**: The state machine the E2E fixture exposes so test classes can observe and assert build + launch progress per Research Decision 4.

**States**:

| State | Meaning |
|-------|---------|
| `NotStarted` | Initial state before `InitializeAsync` runs. |
| `Building` | `dotnet build src/AkmlSql.Engine -c Release` in flight. |
| `BuildFailed` | Build returned non-zero; `BuildOutput` captured; fixture refuses to launch. |
| `Launching` | Engine process spawned; readiness probe is polling. |
| `Ready` | Bridge port accepts a `ws://` connection. Tests run. |
| `LaunchTimedOut` | 30 s elapsed without readiness; fixture throws. |
| `TornDown` | Engine process killed; resources released. |

**Fields**:

| Field | Type | Meaning |
|-------|------|---------|
| `State` | `BridgeE2EFixtureState` | Current state per the table above. |
| `Port` | `int?` | Free port picked via `TcpListener(IPAddress.Loopback, 0)`. Null until `Building` completes. |
| `EngineProcess` | `Process?` | Child process handle. Null outside `Ready`. |
| `BuildOutput` | `string` | Captured stdout+stderr of the `dotnet build` invocation. Empty when build succeeded. |
| `LaunchedAt` | `DateTimeOffset?` | Set when `State == Ready`. Used by tests to compute elapsed times against PRD success-metric budgets. |

**Validation rules**:

- `Port` MUST be in `1024..65535` (matches `WebSocketTransportOptions` validation).
- `EngineProcess.HasExited` MUST be `false` while `State == Ready`. The fixture's `DisposeAsync` MUST kill the process if still running.
- The `[Trait("Category","BridgeE2E")]` attribute MUST be present on every test class that takes this fixture as an `IClassFixture<>` — enforced by an assembly-level test that scans for the trait (out-of-scope to add; convention check only).
