#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace AkmlSql.Core.Config
{
    /// <summary>
    /// Spec 037 (V15, FR-012) — tolerant reader for <see cref="AiSettings.Agents"/>. An entry
    /// that fails to deserialize (<c>42</c>, a string where the <c>health</c> object belongs, a
    /// scalar in place of an agent) is skipped with a warning naming its index, and the rest of
    /// the list loads; without this, one hand-edited entry throws inside
    /// <c>JsonSerializer.Deserialize&lt;AppSettings&gt;</c> before <see cref="AiAgentResolver.Normalize"/>
    /// ever runs, and <c>ConfigManager.Load</c>'s catch-all replaces the whole configuration with
    /// defaults — which the next save then writes over the user's file. A non-array
    /// <c>agents</c> value reads as an empty list (V14 can still rescue the flat fields).
    /// Well-formed-but-invalid entries (null, empty id, duplicate id) deserialize fine and are
    /// dropped later by the resolver's V15 step. Writing is the default serialisation, so a save
    /// round-trips exactly what the tolerant read kept.
    /// </summary>
    public sealed class AiAgentListConverter : JsonConverter<List<AiAgent>>
    {
        public override List<AiAgent> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                reader.Skip();
                return new List<AiAgent>();
            }

            var agents = new List<AiAgent>();
            var index = 0;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray) return agents;

                using var doc = JsonDocument.ParseValue(ref reader);
                try
                {
                    // Parseable-but-null deserializes to a null entry on purpose: V15 drops it
                    // with its own warning, alongside empty-id and duplicate-id entries.
                    agents.Add(JsonSerializer.Deserialize<AiAgent>(doc.RootElement.GetRawText(), options)!);
                }
                catch (JsonException ex)
                {
                    Log.Warning(ex, "AiAgentListConverter: dropping ai.agents[{Index}]: the entry is unparseable", index);
                }
                index++;
            }
            return agents;   // unreachable on well-formed JSON — a truncated stream throws in Read
        }

        public override void Write(Utf8JsonWriter writer, List<AiAgent> value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value, options);
        }
    }
}
