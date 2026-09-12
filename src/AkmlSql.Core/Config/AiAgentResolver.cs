#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Serilog;

namespace AkmlSql.Core.Config
{
    /// <summary>
    /// Spec 037 (data-model E6) — stateless resolution, validation, migration, mirroring and
    /// projection over <see cref="AiSettings.Agents"/>. The rules implemented here live in
    /// <c>contracts/agent-model.md</c>: <c>ai.agents</c> is the truth and the flat
    /// provider/model/apiKey/endpoint fields of <see cref="AiSettings"/> are a derived mirror of
    /// the active agent, rewritten on every load (<see cref="Normalize"/>) and every save
    /// (<see cref="MirrorActiveAgent"/>).
    /// </summary>
    public static class AiAgentResolver
    {
        /// <summary>V12 — the list holds at most this many agents.</summary>
        public const int MaxAgents = 20;

        // ── Load path ────────────────────────────────────────────────────────

        /// <summary>
        /// Load-time normalisation, in rule order: drop malformed entries (V15) and any beyond
        /// the 20-agent ceiling (V21), repair unknown health statuses (V20), canonicalise provider
        /// spellings (FR-014 — before any usability-dependent step runs), migrate the
        /// pre-agents flat-field shape (V14 — after the drops, so a list that lost every entry
        /// is rescued from its flat fields), repair a dangling <see cref="AiSettings.ActiveAgentId"/>
        /// (V13), clear feature assignments naming no usable agent (V16 — recording each clear in
        /// <see cref="AiSettings.ClearedFeatureAssignments"/> for the engine's V22 notice), prune
        /// the fallback order (V17), mirror the active agent into the flat fields (V18) and derive
        /// <see cref="AiSettings.Enabled"/> (V19). Idempotent — every repair establishes a state
        /// it then leaves alone — and never throws: a failure is logged and the settings pass
        /// through as read, because <c>ConfigManager.Load</c> returning defaults on any failure
        /// must not be turned into a lost config by normalisation.
        /// </summary>
        public static void Normalize(AiSettings settings)
        {
            if (settings == null) return;
            try
            {
                // Hand-edited JSON can null out any of these; every step below assumes instances.
                settings.Agents ??= new List<AiAgent>();
                settings.FeatureAgents ??= new FeatureAgentAssignments();
                settings.FallbackOrder ??= new List<string>();

                DropMalformedAgents(settings);        // V15
                DropExcessAgents(settings);           // V21
                RepairHealthStatuses(settings);       // V20
                NormalizeProviderSpellings(settings); // FR-014 — before usability is judged
                MigrateFlatProvider(settings);        // V14
                RepairActiveAgentId(settings);        // V13
                RepairFeatureAssignments(settings);   // V16
                RepairFallbackOrder(settings);        // V17 — after V13: the active id it removes is final
                MirrorActiveAgent(settings);
                settings.Enabled = settings.Agents.Any(IsUsable);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "AiAgentResolver.Normalize: normalisation failed; continuing with the settings as read");
            }
        }

        /// <summary>
        /// V15 — an agent with an empty id, a duplicate id, or an unreadable (null) entry is
        /// dropped with a warning naming its index; the rest load. The first occurrence of a
        /// duplicated id wins, matching V17's keep-the-first rule and edit-time V5's
        /// case-insensitive comparison.
        /// </summary>
        private static void DropMalformedAgents(AiSettings settings)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var kept = new List<AiAgent>(settings.Agents.Count);
            var dropped = false;
            for (var i = 0; i < settings.Agents.Count; i++)
            {
                var agent = settings.Agents[i];
                string reason;
                if (agent == null) reason = "the entry is unreadable";
                else if (string.IsNullOrEmpty(agent.Id)) reason = "the id is empty";
                else if (!seen.Add(agent.Id)) reason = "the id duplicates an earlier agent";
                else
                {
                    kept.Add(agent);
                    continue;
                }
                dropped = true;
                Log.Warning("AiAgentResolver.Normalize: dropping ai.agents[{Index}]: {Reason}", i, reason);
            }
            if (!dropped) return;
            settings.Agents.Clear();
            settings.Agents.AddRange(kept);
        }

        /// <summary>
        /// V21 — a hand-edited file can hold more than <see cref="MaxAgents"/> entries even though
        /// the dialog cannot produce that; the excess is dropped with a warning naming the index.
        /// </summary>
        private static void DropExcessAgents(AiSettings settings)
        {
            while (settings.Agents.Count > MaxAgents)
            {
                var index = settings.Agents.Count - 1;
                Log.Warning("AiAgentResolver.Normalize: dropping ai.agents[{Index}]: beyond the {Max} agent limit", index, MaxAgents);
                settings.Agents.RemoveAt(index);
            }
        }

        /// <summary>
        /// V20 — a health status outside the four known values becomes <c>unknown</c>. Known
        /// values compare case-insensitively on read (data-model E2), so a case variant is kept.
        /// </summary>
        private static void RepairHealthStatuses(AiSettings settings)
        {
            foreach (var agent in settings.Agents)
            {
                var health = agent?.Health;
                if (health == null) continue;
                if (!IsKnownHealthStatus(health.Status))
                    health.Status = AgentHealthStatus.Unknown;
            }
        }

        private static bool IsKnownHealthStatus(string? status)
        {
            return string.Equals(status, AgentHealthStatus.Unknown, StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, AgentHealthStatus.Ready, StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, AgentHealthStatus.NeedsKey, StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, AgentHealthStatus.Failed, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// FR-014 — an agent persisted with a legacy or display provider spelling
        /// (<c>"AzureOpenAI"</c>, <c>"moonshot"</c>, <c>"LMStudio"</c>) is canonicalised on read.
        /// Runs before the usability-dependent repairs: <see cref="IsUsable"/> rejects any
        /// non-canonical id, so an uncanonicalised agent would be judged unusable and lose its
        /// feature assignments (V16) and fallback entries (V17).
        /// </summary>
        private static void NormalizeProviderSpellings(AiSettings settings)
        {
            foreach (var agent in settings.Agents)
            {
                if (agent?.Provider != null)
                    agent.Provider = AiProviderIds.Normalize(agent.Provider);
            }
        }

        /// <summary>
        /// V13 — an <see cref="AiSettings.ActiveAgentId"/> naming no agent in the list becomes the
        /// id of the first usable agent, or <c>""</c> when there is none. An id naming a present
        /// but disabled agent is the user's own state and is kept; an empty id is the product's
        /// default, not corruption, and stays empty.
        /// </summary>
        private static void RepairActiveAgentId(AiSettings settings)
        {
            var id = settings.ActiveAgentId;
            if (string.IsNullOrEmpty(id))
            {
                settings.ActiveAgentId = "";   // a JSON null becomes the canonical empty
                return;
            }
            if (Find(settings, id) != null) return;

            var firstUsable = settings.Agents.FirstOrDefault(IsUsable);
            settings.ActiveAgentId = firstUsable?.Id ?? "";
        }

        /// <summary>
        /// V16 — a feature assignment naming no usable agent (missing or disabled) reverts to
        /// <c>""</c>, "follow the active agent" (FR-049). Each cleared feature is recorded in
        /// <see cref="AiSettings.ClearedFeatureAssignments"/> as the post-load signal for the
        /// engine's once-per-feature fallback notice (V22): by the time a handler runs, the
        /// dangling assignment it would have keyed on is already gone.
        /// </summary>
        private static void RepairFeatureAssignments(AiSettings settings)
        {
            var assignments = settings.FeatureAgents;
            assignments.Chat = RepairedAssignment(settings, assignments.Chat, nameof(AiFeature.Chat));
            assignments.TextToSql = RepairedAssignment(settings, assignments.TextToSql, nameof(AiFeature.TextToSql));
            assignments.Explain = RepairedAssignment(settings, assignments.Explain, nameof(AiFeature.Explain));
            assignments.Fix = RepairedAssignment(settings, assignments.Fix, nameof(AiFeature.Fix));
            assignments.Optimize = RepairedAssignment(settings, assignments.Optimize, nameof(AiFeature.Optimize));
            assignments.IndexSuggestions = RepairedAssignment(settings, assignments.IndexSuggestions, nameof(AiFeature.IndexSuggestions));
            assignments.GhostText = RepairedAssignment(settings, assignments.GhostText, nameof(AiFeature.GhostText));
        }

        private static string RepairedAssignment(AiSettings settings, string? id, string featureName)
        {
            if (string.IsNullOrEmpty(id)) return "";
            var agent = Find(settings, id!);
            if (agent != null && IsUsable(agent)) return id!;
            if (!settings.ClearedFeatureAssignments.Contains(featureName))
                settings.ClearedFeatureAssignments.Add(featureName);
            return "";
        }

        /// <summary>
        /// V17 — fallback entries naming no usable agent are removed, duplicates are removed
        /// keeping the first, and the active agent's own id is removed: an agent is never its
        /// own fallback.
        /// </summary>
        private static void RepairFallbackOrder(AiSettings settings)
        {
            var order = settings.FallbackOrder;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var kept = new List<string>(order.Count);
            var changed = false;
            foreach (var id in order)
            {
                var keep = !string.IsNullOrEmpty(id)
                    && !string.Equals(id, settings.ActiveAgentId, StringComparison.Ordinal)
                    && seen.Add(id)
                    && IsUsableId(settings, id);
                if (keep)
                {
                    kept.Add(id);
                }
                else
                {
                    changed = true;
                }
            }
            if (!changed) return;
            order.Clear();
            order.AddRange(kept);
        }

        private static bool IsUsableId(AiSettings settings, string id)
        {
            var agent = Find(settings, id);
            return agent != null && IsUsable(agent);
        }

        /// <summary>
        /// V14 — a pre-agents config (flat fields populated, no agent list) becomes exactly one
        /// agent named for its provider's display name, enabled, never health-tested, and active.
        /// The API key is carried across <b>verbatim</b>: unwrapping it here would fail on a
        /// roamed profile and destroy a working key, and re-wrapping would corrupt it. A
        /// whitespace-only <see cref="AiSettings.Provider"/> is no provider at all — migrating it
        /// would produce a permanently unusable blank agent.
        /// </summary>
        private static void MigrateFlatProvider(AiSettings settings)
        {
            if (settings.Agents.Count != 0 || string.IsNullOrWhiteSpace(settings.Provider)) return;

            var provider = AiProviderIds.Normalize(settings.Provider);
            var agent = new AiAgent
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = AiProviderIds.DisplayName(provider),
                Provider = provider,
                Model = settings.Model,
                ApiKey = settings.ApiKey,   // VERBATIM — never unwrap, never re-wrap
                Endpoint = settings.Endpoint,
                MaxTokens = settings.MaxTokens,
                Temperature = settings.Temperature,
                Timeout = settings.Timeout,
                Retries = settings.Retries,
                Enabled = true,
                CreatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                Health = null,
            };
            settings.Agents.Add(agent);
            settings.ActiveAgentId = agent.Id;
        }

        // ── Mirroring (V18) ──────────────────────────────────────────────────

        /// <summary>
        /// Rewrites the flat connection fields of <paramref name="settings"/> from the active
        /// agent, so anything reading the JSON directly (an older build after a downgrade, a
        /// second host that has not reloaded) sees the same provider the new build uses. Called
        /// by <see cref="Normalize"/> on load and by <c>ConfigManager.Save</c> on write.
        /// Idempotent. Never touches <see cref="AiSettings.Enabled"/> — that flag is derived by
        /// <see cref="Normalize"/> only, so the write path stays safe to call on settings a
        /// caller assembled by hand.
        /// </summary>
        public static void MirrorActiveAgent(AiSettings settings)
        {
            if (settings == null) return;
            var agents = settings.Agents;
            if (agents == null || agents.Count == 0)
            {
                // The empty-list guard is load-bearing: flat fields with no agent list are the
                // pre-migration shape V14 rescues on the next load. Blanking them here would
                // destroy the very configuration migration exists to rescue.
                return;
            }

            var active = Active(settings);
            if (active != null)
            {
                settings.Provider = active.Provider;
                settings.Model = active.Model;
                settings.ApiKey = active.ApiKey;   // still wrapped; the factory's KeyDecryptor unwraps
                settings.Endpoint = active.Endpoint;
                settings.MaxTokens = active.MaxTokens;
                settings.Temperature = active.Temperature;
                settings.Timeout = active.Timeout;
                settings.Retries = active.Retries;
            }
            else
            {
                settings.Provider = settings.Model = settings.ApiKey = settings.Endpoint = "";
                // Request parameters keep their current values — harmless without a provider.
            }
        }

        // ── Projection (FR-048) ──────────────────────────────────────────────

        /// <summary>
        /// A copy of <paramref name="global"/> with the agent's connection fields and request
        /// parameters substituted and every global concern (privacy, consent, feature switches,
        /// offline fields, shortcuts) preserved. The result is handed to
        /// <c>AiProviderFactory.Create</c> unchanged — the same manoeuvre
        /// <c>CreateFromFallback</c> performs with the offline fields. The mutable members
        /// (<see cref="AiSettings.Agents"/>, <see cref="AiSettings.FeatureAgents"/>,
        /// <see cref="AiSettings.FallbackOrder"/>) are copied, not aliased — the agent list
        /// deep-copied element by element: a handler mutating an agent reached through the
        /// projection must never reach the cached global settings.
        /// </summary>
        public static AiSettings Project(AiSettings global, AiAgent agent)
        {
            if (global == null) throw new ArgumentNullException(nameof(global));
            if (agent == null) throw new ArgumentNullException(nameof(agent));

            return new AiSettings
            {
                // Connection fields and request parameters come from the agent.
                Provider = agent.Provider,
                Model = agent.Model,
                ApiKey = agent.ApiKey,
                Endpoint = agent.Endpoint,
                MaxTokens = agent.MaxTokens,
                Temperature = agent.Temperature,
                Timeout = agent.Timeout,
                Retries = agent.Retries,

                // Everything else is a global concern and comes from the settings.
                Enabled = global.Enabled,
                SchemaContextMaxObjects = global.SchemaContextMaxObjects,
                PrivacyMode = global.PrivacyMode,
                OfflineProvider = global.OfflineProvider,
                OfflineModel = global.OfflineModel,
                OfflineEndpoint = global.OfflineEndpoint,
                TextToSql = global.TextToSql,
                Explain = global.Explain,
                Fix = global.Fix,
                AutoFixOnError = global.AutoFixOnError,
                Optimize = global.Optimize,
                IndexSuggestions = global.IndexSuggestions,
                InlineCompletion = global.InlineCompletion,
                ChatPanel = global.ChatPanel,
                PrivacyConsentRequired = global.PrivacyConsentRequired,
                OpenChatShortcut = global.OpenChatShortcut,
                FixShortcut = global.FixShortcut,
                OptimizeShortcut = global.OptimizeShortcut,
                GhostTextShortcut = global.GhostTextShortcut,
                ShowEditorIcon = global.ShowEditorIcon,
                ShowFollowupSuggestions = global.ShowFollowupSuggestions,
                CommentTriggerPrefix = global.CommentTriggerPrefix,
                GhostTextDelayMs = global.GhostTextDelayMs,

                // Agent bookkeeping stays the global's, copied so nothing is aliased.
                ActiveAgentId = global.ActiveAgentId,
                Agents = CopyAgents(global.Agents),
                FallbackOrder = global.FallbackOrder != null ? new List<string>(global.FallbackOrder) : new List<string>(),
                FeatureAgents = global.FeatureAgents != null
                    ? new FeatureAgentAssignments
                    {
                        Chat = global.FeatureAgents.Chat,
                        TextToSql = global.FeatureAgents.TextToSql,
                        Explain = global.FeatureAgents.Explain,
                        Fix = global.FeatureAgents.Fix,
                        Optimize = global.FeatureAgents.Optimize,
                        IndexSuggestions = global.FeatureAgents.IndexSuggestions,
                        GhostText = global.FeatureAgents.GhostText,
                    }
                    : new FeatureAgentAssignments(),
            };
        }

        /// <summary>
        /// A deep copy of the agent list for <see cref="Project"/>: a fresh list alone would still
        /// alias the <see cref="AiAgent"/> elements, and a handler mutating one of them would
        /// rewrite the cached global settings. Agents are small (and ≤ <see cref="MaxAgents"/>),
        /// so a field-by-field copy is cheap.
        /// </summary>
        private static List<AiAgent> CopyAgents(List<AiAgent>? agents)
        {
            var copy = new List<AiAgent>(agents?.Count ?? 0);
            if (agents == null) return copy;
            foreach (var agent in agents)
                copy.Add(agent == null ? null! : CopyAgent(agent));
            return copy;
        }

        private static AiAgent CopyAgent(AiAgent agent)
        {
            return new AiAgent
            {
                Id = agent.Id,
                Name = agent.Name,
                Provider = agent.Provider,
                Model = agent.Model,
                ApiKey = agent.ApiKey,
                Endpoint = agent.Endpoint,
                MaxTokens = agent.MaxTokens,
                Temperature = agent.Temperature,
                Timeout = agent.Timeout,
                Retries = agent.Retries,
                Enabled = agent.Enabled,
                CreatedUtc = agent.CreatedUtc,
                Health = agent.Health == null
                    ? null
                    : new AgentHealth
                    {
                        Status = agent.Health.Status,
                        CheckedUtc = agent.Health.CheckedUtc,
                        LatencyMs = agent.Health.LatencyMs,
                        Message = agent.Health.Message,
                    },
                KeyDecryptFailed = agent.KeyDecryptFailed,
            };
        }

        // ── Resolution ───────────────────────────────────────────────────────

        /// <summary>
        /// The agent serving <paramref name="feature"/>: the assigned agent when one is assigned
        /// and usable, else the active agent when usable, else <c>null</c> (S3).
        /// </summary>
        public static AiAgent? ResolveFor(AiSettings settings, AiFeature feature)
        {
            if (settings == null) return null;
            var assignedId = AssignedIdFor(settings, feature);

            if (!string.IsNullOrEmpty(assignedId))
            {
                var assigned = Find(settings, assignedId!);
                if (assigned != null && IsUsable(assigned)) return assigned;
            }

            return Active(settings);
        }

        /// <summary>
        /// The raw assignment for <paramref name="feature"/> — an agent id, or <c>""</c> for
        /// "follow the active agent". Exposed so the engine can tell "assigned but unusable"
        /// (fall back to the active agent, FR-049/V22) apart from "never assigned".
        /// </summary>
        public static string? AssignedIdFor(AiSettings settings, AiFeature feature)
        {
            var assignments = settings?.FeatureAgents;
            return feature switch
            {
                AiFeature.Chat => assignments?.Chat,
                AiFeature.TextToSql => assignments?.TextToSql,
                AiFeature.Explain => assignments?.Explain,
                AiFeature.Fix => assignments?.Fix,
                AiFeature.Optimize => assignments?.Optimize,
                AiFeature.IndexSuggestions => assignments?.IndexSuggestions,
                AiFeature.GhostText => assignments?.GhostText,
                _ => null,
            };
        }

        /// <summary>The agent named by <see cref="AiSettings.ActiveAgentId"/>, if usable; else <c>null</c>.</summary>
        public static AiAgent? Active(AiSettings settings)
        {
            if (settings == null) return null;
            var id = settings.ActiveAgentId;
            if (string.IsNullOrEmpty(id)) return null;
            var agent = Find(settings, id);
            return agent != null && IsUsable(agent) ? agent : null;
        }

        private static AiAgent? Find(AiSettings settings, string id)
        {
            var agents = settings.Agents;
            if (agents == null) return null;
            foreach (var agent in agents)
            {
                if (agent != null && string.Equals(agent.Id, id, StringComparison.Ordinal))
                    return agent;
            }
            return null;
        }

        // ── Usability (S1) ───────────────────────────────────────────────────

        /// <summary>
        /// S1 — an agent is usable exactly when it is enabled, names a canonical provider, has a
        /// model, and carries every field its provider requires (key for the cloud providers,
        /// endpoint for azure/custom). The chat empty state fires exactly when no agent is usable.
        /// </summary>
        public static bool IsUsable(AiAgent agent)
        {
            if (agent == null || !agent.Enabled) return false;
            var provider = agent.Provider;
            if (string.IsNullOrEmpty(provider)) return false;
            if (!AiProviderIds.CanonicalIds.Contains(provider)) return false;
            if (string.IsNullOrWhiteSpace(agent.Model)) return false;
            if (RequiresApiKey(provider) && string.IsNullOrEmpty(agent.ApiKey)) return false;
            if (RequiresEndpoint(provider) && string.IsNullOrEmpty(agent.Endpoint)) return false;
            return true;
        }

        /// <summary><c>true</c> for the providers that cannot serve a request without an API key.</summary>
        public static bool RequiresApiKey(string providerId)
        {
            switch (providerId)
            {
                case AiProviderIds.Anthropic:
                case AiProviderIds.OpenAI:
                case AiProviderIds.Azure:
                case AiProviderIds.Gemini:
                case AiProviderIds.Kimi:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary><c>true</c> for the providers that cannot serve a request without an endpoint URL.</summary>
        public static bool RequiresEndpoint(string providerId)
        {
            return providerId == AiProviderIds.Azure || providerId == AiProviderIds.Custom;
        }

        // ── Naming ───────────────────────────────────────────────────────────

        /// <summary>"Agent N" with the lowest free N ≥ 1, compared case-insensitively after trim (V3).</summary>
        public static string SuggestName(IEnumerable<AiAgent> agents)
        {
            var taken = TakenNames(agents);
            for (var n = 1; ; n++)
            {
                var candidate = "Agent " + n.ToString(CultureInfo.InvariantCulture);
                if (!taken.Contains(candidate)) return candidate;
            }
        }

        /// <summary>"&lt;name&gt; (copy)", then "&lt;name&gt; (copy 2)" and so on until free.</summary>
        public static string SuggestCopyName(IEnumerable<AiAgent> agents, string name)
        {
            var taken = TakenNames(agents);
            var candidate = name + " (copy)";
            if (!taken.Contains(candidate)) return candidate;
            for (var n = 2; ; n++)
            {
                candidate = name + " (copy " + n.ToString(CultureInfo.InvariantCulture) + ")";
                if (!taken.Contains(candidate)) return candidate;
            }
        }

        private static HashSet<string> TakenNames(IEnumerable<AiAgent> agents)
        {
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (agents == null) return taken;
            foreach (var agent in agents)
            {
                if (agent?.Name != null) taken.Add(agent.Name.Trim());
            }
            return taken;
        }

        // ── Edit-time validation (V1–V12) ────────────────────────────────────

        /// <summary>V12 — whether another agent may be added to a list of <paramref name="currentCount"/>.</summary>
        public static bool CanAddAgent(int currentCount) => currentCount < MaxAgents;

        /// <summary>
        /// The edit-time rules V1–V11, exposed for the Options page (FR-032): returns a
        /// user-facing message naming the failing field, or <c>null</c> when the agent is valid.
        /// <paramref name="all"/> is the whole working list — uniqueness (V3, V5) is checked
        /// against every entry other than <paramref name="agent"/> itself. V12 is a list-level
        /// rule; see <see cref="CanAddAgent"/>.
        /// </summary>
        /// <param name="keyDecryptFailed">
        /// V8/V23 — when the stored key could not be unwrapped on this machine it stands as-is
        /// and the agent is reported <see cref="AgentHealthStatus.NeedsKey"/> instead of failing
        /// validation for an empty key box.
        /// </param>
        public static string? Validate(AiAgent agent, IReadOnlyList<AiAgent> all, bool keyDecryptFailed = false)
        {
            if (agent == null) return "Agent is missing.";

            var name = (agent.Name ?? "").Trim();
            if (name.Length == 0) return "Name is required.";                                                   // V1
            if (name.Length > 40) return "Name must be 40 characters or fewer.";                                // V2
            foreach (var other in Others(agent, all))
            {
                if (string.Equals((other.Name ?? "").Trim(), name, StringComparison.OrdinalIgnoreCase))
                    return $"Another agent is already named \"{name}\".";                                       // V3
            }

            if (!AiProviderIds.CanonicalIds.Contains(agent.Provider ?? ""))
                return $"Provider \"{agent.Provider}\" is not a supported AI provider.";                        // V4

            if (string.IsNullOrEmpty(agent.Id) || !IsHex32(agent.Id))
                return "Id must be 32 hexadecimal characters.";                                                 // V5
            foreach (var other in Others(agent, all))
            {
                if (string.Equals(other.Id, agent.Id, StringComparison.OrdinalIgnoreCase))
                    return "Another agent already uses this id.";                                               // V5
            }

            if (string.IsNullOrWhiteSpace(agent.Model)) return "Model is required.";                            // V6

            var provider = agent.Provider ?? "";
            var family = AiModelFamily.Detect(agent.Model);
            if ((provider == AiProviderIds.Anthropic || provider == AiProviderIds.OpenAI ||
                 provider == AiProviderIds.Gemini || provider == AiProviderIds.Kimi) &&
                family != null && family != provider)
            {
                return $"Model \"{agent.Model}\" is a {family} model, not a {provider} model.";                 // V7
            }

            if (!keyDecryptFailed && RequiresApiKey(provider) && string.IsNullOrEmpty(agent.ApiKey))
                return "API key is required for this provider.";                                                // V8

            if (RequiresEndpoint(provider) && string.IsNullOrEmpty(agent.Endpoint))
                return "Endpoint is required for this provider.";                                               // V9

            if (!string.IsNullOrEmpty(agent.Endpoint) &&
                !Uri.TryCreate(agent.Endpoint, UriKind.Absolute, out _))
            {
                return "Endpoint must be an absolute URL.";                                                     // V10
            }

            if (agent.MaxTokens < 256 || agent.MaxTokens > 32768)
                return "Max tokens must be between 256 and 32768.";                                             // V11
            if (agent.Temperature < 0.0 || agent.Temperature > 2.0)
                return "Temperature must be between 0.0 and 2.0.";                                              // V11
            if (agent.Timeout < 5 || agent.Timeout > 300)
                return "Timeout must be between 5 and 300 seconds.";                                            // V11
            if (agent.Retries < 0 || agent.Retries > 5)
                return "Retries must be between 0 and 5.";                                                      // V11

            return null;
        }

        private static IEnumerable<AiAgent> Others(AiAgent agent, IReadOnlyList<AiAgent>? all)
        {
            if (all == null) yield break;
            foreach (var other in all)
            {
                if (other != null && !ReferenceEquals(other, agent)) yield return other;
            }
        }

        private static bool IsHex32(string id)
        {
            if (id.Length != 32) return false;
            foreach (var c in id)
            {
                var isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!isHex) return false;
            }
            return true;
        }
    }
}
