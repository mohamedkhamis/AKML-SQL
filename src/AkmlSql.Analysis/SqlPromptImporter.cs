using System.Text.Json;
using System.Xml;
using AkmlSql.Core.Models.Analysis;
using Serilog;

namespace AkmlSql.Engine.Analysis;

/// <summary>
/// Imports SQL Prompt CAsettings XML files and converts them to the AKML .casettings JSON format.
/// The SQL Prompt format uses &lt;CASetting&gt; elements with a Name attribute (SQL Prompt rule ID)
/// and a Value attribute ("enabled" / "warning" / "error" / "disabled").
/// </summary>
public static class SqlPromptImporter
{
    // Map from SQL Prompt rule IDs → AKML rule IDs (same or similar semantics)
    private static readonly Dictionary<string, string> IdMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "BP001", "BP001" }, { "BP002", "BP002" }, { "BP003", "BP003" }, { "BP004", "BP004" },
        { "BP005", "BP005" }, { "BP006", "BP006" }, { "BP007", "BP007" }, { "BP008", "BP008" },
        { "BP009", "BP009" }, { "BP010", "BP010" }, { "BP011", "BP011" }, { "BP012", "BP012" },
        { "BP013", "BP013" }, { "BP014", "BP014" }, { "BP015", "BP015" }, { "BP016", "BP016" },
        { "BP017", "BP017" }, { "BP018", "BP018" },
        { "PE001", "PE001" }, { "PE002", "PE002" }, { "PE003", "PE003" }, { "PE004", "PE004" },
        { "PE005", "PE005" }, { "PE006", "PE006" }, { "PE007", "PE007" }, { "PE008", "PE008" },
        { "PE009", "PE009" }, { "PE010", "PE010" }, { "PE011", "PE011" }, { "PE012", "PE012" },
        { "PE013", "PE013" },
        { "DEP001", "DEP001" }, { "DEP002", "DEP002" }, { "DEP003", "DEP003" }, { "DEP004", "DEP004" },
        { "DEP005", "DEP005" }, { "DEP006", "DEP006" }, { "DEP007", "DEP007" }, { "DEP008", "DEP008" },
        { "DEP009", "DEP009" }, { "DEP010", "DEP010" },
        { "ST001",  "ST001"  }, { "ST002",  "ST002"  }, { "ST003",  "ST003"  }, { "ST004",  "ST004"  },
        { "ST005",  "ST005"  }, { "ST006",  "ST006"  }, { "ST007",  "ST007"  }, { "ST008",  "ST008"  },
        { "ST009",  "ST009"  }, { "ST010",  "ST010"  }, { "ST011",  "ST011"  },
        { "EX001",  "EX001"  }, { "EX002",  "EX002"  }, { "EX003",  "EX003"  }, { "EX004",  "EX004"  },
        { "EX005",  "EX005"  }, { "EX006",  "EX006"  },
    };

    /// <summary>
    /// Reads a SQL Prompt CAsettings XML file and writes an AKML .casettings JSON file.
    /// </summary>
    /// <param name="xmlInputPath">Path to the SQL Prompt XML settings file.</param>
    /// <param name="jsonOutputPath">Path where the converted .casettings JSON will be written.</param>
    /// <returns>Number of rules converted; skipped rules are logged at Debug level.</returns>
    public static int Convert(string xmlInputPath, string jsonOutputPath)
    {
        if (!File.Exists(xmlInputPath))
            throw new FileNotFoundException("SQL Prompt settings file not found.", xmlInputPath);

        var rules   = new Dictionary<string, RuleConfig>(StringComparer.OrdinalIgnoreCase);
        int skipped = 0;

        var doc = new XmlDocument();
        doc.Load(xmlInputPath);

        // SQL Prompt format: <CASettings><CASetting Name="BP001" Value="enabled" /></CASettings>
        var nodes = doc.SelectNodes("//CASetting");
        if (nodes != null)
        {
            foreach (XmlNode node in nodes)
            {
                var name  = node.Attributes?["Name"]?.Value  ?? node.Attributes?["name"]?.Value;
                var value = node.Attributes?["Value"]?.Value ?? node.Attributes?["value"]?.Value;

                if (string.IsNullOrEmpty(name))
                    continue;

                if (!IdMap.TryGetValue(name, out var akmlId))
                {
                    Log.Debug("SqlPromptImporter: rule '{Rule}' has no AKML mapping — skipped", name);
                    skipped++;
                    continue;
                }

                rules[akmlId] = MapValue(value);
            }
        }

        var ca  = new CaSettings { Rules = rules };
        var opt = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(jsonOutputPath, JsonSerializer.Serialize(ca, opt));

        Log.Information("SqlPromptImporter: converted {Count} rules ({Skipped} skipped) → {Output}",
            rules.Count, skipped, jsonOutputPath);

        return rules.Count;
    }

    private static RuleConfig MapValue(string? value)
    {
        return (value ?? string.Empty).ToLowerInvariant() switch
        {
            "disabled" or "ignore" or "off" => new RuleConfig { Enabled = false, Severity = "ignore" },
            "error" => new RuleConfig { Enabled = true, Severity = "error" },
            "warning" or "warn" => new RuleConfig { Enabled = true, Severity = "warning" },
            "information" or "info" => new RuleConfig { Enabled = true, Severity = "information" },
            _ => new RuleConfig { Enabled = true, Severity = "warning" }
        };
    }
}
