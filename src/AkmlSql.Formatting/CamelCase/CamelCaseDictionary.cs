using System.Text;

namespace AkmlSql.Formatting.CamelCase;

/// <summary>
/// Word-boundary detection for splitting compound identifiers into PascalCase or camelCase.
/// Uses a common English word list plus heuristics to split identifiers like
/// "customerorderid" into "CustomerOrderId" or "customerOrderId".
/// </summary>
public static class CamelCaseDictionary
{
    // Common words found in database identifiers, ordered longest-first within each length
    // to prefer greedy matching (e.g., "order" before "or").
    private static readonly HashSet<string> CommonWords = new(StringComparer.OrdinalIgnoreCase)
    {
        // 2-letter
        "id", "no", "is", "by", "to", "on", "at", "in", "of", "up",

        // 3-letter
        "key", "log", "map", "max", "min", "new", "old", "qty", "ref",
        "row", "seq", "set", "sql", "src", "sum", "tag", "tax", "url",
        "val", "zip",

        // 4-letter
        "addr", "area", "auth", "auto", "bank", "base", "bill", "blog",
        "body", "bulk", "calc", "card", "cart", "cash", "city", "code",
        "comm", "comp", "conf", "copy", "cost", "curr", "data", "date",
        "days", "desc", "dept", "disc", "docs", "done", "each", "edit",
        "expr", "fact", "file", "find", "flag", "form", "full", "func",
        "hash", "help", "hist", "home", "host", "icon", "info", "init",
        "item", "join", "last", "left", "line", "link", "list", "load",
        "lock", "main", "menu", "meta", "mode", "move", "name", "next",
        "node", "note", "null", "open", "page", "paid", "part", "pass",
        "path", "plan", "post", "prev", "prod", "rank", "rate", "real",
        "role", "rule", "sale", "save", "ship", "show", "sign", "site",
        "size", "slot", "sort", "spec", "step", "stop", "sync", "task",
        "temp", "term", "test", "text", "tier", "time", "todo", "tool",
        "tree", "trim", "type", "unit", "user", "vars", "view", "void",
        "wage", "work", "year", "zone",

        // 5-letter
        "abort", "above", "admin", "after", "agent", "alias", "allow",
        "apply", "asset", "audit", "batch", "begin", "below", "block",
        "bonus", "build", "cache", "carry", "chart", "check", "child",
        "claim", "class", "clean", "clear", "close", "color", "count",
        "cover", "daily", "debug", "delta", "depth", "draft", "email",
        "empty", "entry", "error", "event", "extra", "field", "first",
        "fixed", "float", "floor", "flush", "force", "found", "fresh",
        "given", "grant", "gross", "group", "guard", "guest", "guide",
        "hours", "image", "index", "inner", "input", "inter", "issue",
        "label", "large", "layer", "legal", "level", "limit", "local",
        "login", "lower", "major", "match", "media", "merge", "minor",
        "model", "money", "month", "multi", "night", "notes", "offer",
        "order", "other", "outer", "owner", "panel", "param", "parse",
        "party", "phone", "pivot", "place", "plant", "point", "power",
        "price", "print", "prior", "proof", "query", "queue", "quota",
        "range", "ratio", "realm", "reply", "retry", "right", "round",
        "route", "scope", "score", "setup", "share", "shift", "short",
        "since", "small", "space", "staff", "stage", "start", "state",
        "stock", "store", "style", "table", "title", "token", "total",
        "trace", "trade", "trend", "trial", "trust", "tuple", "union",
        "upper", "usage", "valid", "value", "watch", "where", "width",
        "yield",

        // 6-letter
        "access", "action", "active", "actual", "amount", "basket",
        "before", "budget", "cancel", "change", "charge", "client",
        "column", "commit", "config", "create", "credit", "custom",
        "damage", "debit", "decode", "delete", "demand", "deploy",
        "detail", "device", "direct", "domain", "driver", "enable",
        "encode", "engine", "entity", "escape", "export", "factor",
        "filter", "finish", "folder", "format", "global", "header",
        "height", "hidden", "ignore", "import", "income", "insert",
        "invite", "layout", "length", "linked", "locale", "lookup",
        "manual", "mapper", "margin", "market", "markup", "master",
        "member", "method", "middle", "minute", "mobile", "module",
        "number", "object", "office", "online", "option", "origin",
        "output", "parent", "period", "person", "policy", "postal",
        "prefix", "public", "rating", "reason", "record", "region",
        "reject", "reload", "remote", "remove", "rename", "render",
        "repeat", "report", "result", "return", "review", "revoke",
        "salary", "sample", "schema", "script", "search", "second",
        "secret", "secure", "select", "sender", "serial", "server",
        "single", "source", "status", "string", "submit", "suffix",
        "supply", "switch", "symbol", "system", "target", "tenant",
        "ticket", "toggle", "unique", "update", "upload", "vendor",
        "verify", "weight", "worker",

        // 7+ letter (common in DB identifiers)
        "account", "address", "balance", "billing", "booking", "browser",
        "catalog", "category", "channel", "comment", "company", "contact",
        "content", "context", "control", "counter", "country", "created",
        "current", "customer", "default", "defined", "deleted", "delivery",
        "deposit", "display", "district", "document", "element", "enabled",
        "encrypt", "expense", "expired", "factory", "feature", "finance",
        "freight", "general", "handler", "history", "holiday", "hosting",
        "invoice", "journal", "keyword", "language", "library", "license",
        "limited", "listing", "location", "manager", "mapping", "maximum",
        "message", "minimum", "modified", "network", "notification",
        "numeric", "package", "partner", "patient", "payment", "pending",
        "percent", "permission", "picture", "position", "premium",
        "primary", "priority", "private", "process", "product", "profile",
        "program", "project", "promise", "promote", "provider", "publish",
        "purchase", "receipt", "receive", "reference", "refresh", "related",
        "release", "request", "require", "reserve", "resource", "response",
        "restore", "revenue", "rollback", "running", "runtime", "schedule",
        "security", "segment", "service", "session", "setting", "shipping",
        "signature", "standard", "startup", "storage", "subject", "summary",
        "support", "suspend", "template", "terminal", "timeout", "tracking",
        "transaction", "transfer", "trigger", "updated", "upgrade", "version",
        "visible", "warning", "website", "workflow",
    };

    /// <summary>
    /// Splits a compound identifier using dictionary-based word boundary detection
    /// and applies the specified casing mode.
    /// </summary>
    /// <param name="identifier">The identifier to process (e.g., "customerorderid")</param>
    /// <param name="mode">"PascalCase" or "camelCase"</param>
    /// <returns>The cased identifier (e.g., "CustomerOrderId" or "customerOrderId")</returns>
    public static string Apply(string identifier, string mode)
    {
        if (string.IsNullOrEmpty(identifier))
            return identifier;

        // If it already has underscores, split on those
        if (identifier.Contains('_'))
        {
            return ApplyToUnderscored(identifier, mode);
        }

        // If it already has mixed case (camelCase/PascalCase boundaries), respect them
        if (HasMixedCase(identifier))
        {
            return ApplyToMixedCase(identifier, mode);
        }

        // Pure lowercase or uppercase — try dictionary splitting
        var words = SplitByDictionary(identifier.ToLowerInvariant());
        return AssembleWords(words, mode);
    }

    private static string ApplyToUnderscored(string identifier, string mode)
    {
        var parts = identifier.Split('_');
        var result = new StringBuilder();

        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0)
            {
                result.Append('_');
                continue;
            }

            var word = parts[i].ToLowerInvariant();
            if (i == 0 && mode == "camelCase")
                result.Append(word);
            else
                result.Append(char.ToUpperInvariant(word[0])).Append(word[1..]);
        }

        return result.ToString();
    }

    private static string ApplyToMixedCase(string identifier, string mode)
    {
        // Split on existing case boundaries
        var words = SplitOnCaseBoundaries(identifier);
        return AssembleWords(words, mode);
    }

    private static bool HasMixedCase(string text)
    {
        bool hasUpper = false;
        bool hasLower = false;

        foreach (char c in text)
        {
            if (char.IsUpper(c)) hasUpper = true;
            if (char.IsLower(c)) hasLower = true;
            if (hasUpper && hasLower) return true;
        }

        return false;
    }

    private static List<string> SplitOnCaseBoundaries(string text)
    {
        var words = new List<string>();
        int start = 0;

        for (int i = 1; i < text.Length; i++)
        {
            // Boundary: lowercase→uppercase (e.g., "customerId" → "customer", "Id")
            if (char.IsLower(text[i - 1]) && char.IsUpper(text[i]))
            {
                words.Add(text[start..i].ToLowerInvariant());
                start = i;
            }
            // Boundary: multiple uppercase followed by lowercase (e.g., "HTTPSPort" → "HTTPS", "Port")
            else if (i >= 2 && char.IsUpper(text[i - 2]) && char.IsUpper(text[i - 1]) && char.IsLower(text[i]))
            {
                words.Add(text[start..(i - 1)].ToLowerInvariant());
                start = i - 1;
            }
        }

        if (start < text.Length)
            words.Add(text[start..].ToLowerInvariant());

        return words;
    }

    /// <summary>
    /// Greedy dictionary-based splitting. Tries to match the longest known word
    /// at each position. Falls back to single characters for unmatched segments.
    /// </summary>
    internal static List<string> SplitByDictionary(string lower)
    {
        var words = new List<string>();
        int pos = 0;

        while (pos < lower.Length)
        {
            string? bestMatch = null;
            int bestLen = 0;

            // Try all possible lengths from current position, longest first
            int maxLen = Math.Min(lower.Length - pos, 15); // Max word length in dictionary
            for (int len = maxLen; len >= 2; len--)
            {
                var candidate = lower.Substring(pos, len);
                if (CommonWords.Contains(candidate))
                {
                    bestMatch = candidate;
                    bestLen = len;
                    break;
                }
            }

            if (bestMatch != null)
            {
                words.Add(bestMatch);
                pos += bestLen;
            }
            else
            {
                // No dictionary match — accumulate single chars into a segment
                int segStart = pos;
                pos++;
                // Keep going until we find a dictionary match
                while (pos < lower.Length)
                {
                    bool found = false;
                    int remainLen = Math.Min(lower.Length - pos, 15);
                    for (int len = remainLen; len >= 2; len--)
                    {
                        if (CommonWords.Contains(lower.Substring(pos, len)))
                        {
                            found = true;
                            break;
                        }
                    }
                    if (found) break;
                    pos++;
                }
                words.Add(lower[segStart..pos]);
            }
        }

        return words;
    }

    private static string AssembleWords(List<string> words, string mode)
    {
        if (words.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        for (int i = 0; i < words.Count; i++)
        {
            var w = words[i];
            if (w.Length == 0) continue;

            if (i == 0 && mode == "camelCase")
            {
                sb.Append(w.ToLowerInvariant());
            }
            else
            {
                sb.Append(char.ToUpperInvariant(w[0]));
                if (w.Length > 1)
                    sb.Append(w[1..].ToLowerInvariant());
            }
        }

        return sb.ToString();
    }
}
