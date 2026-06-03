using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AkmlSql.Web.Services;

/// <summary>One turn of a persisted conversation. <see cref="ProviderId"/> records which
/// provider produced the turn (FR-033) — empty for user turns.</summary>
public sealed class ChatTurn
{
    public string Role { get; set; } = "user";   // "user" | "assistant"
    public string Content { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
}

/// <summary>A locally-persisted AI chat conversation (data-model E8).</summary>
public sealed class ChatConversation
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<ChatTurn> Turns { get; set; } = new();

    /// <summary>Render the conversation as Markdown (FR-031). Turn content is emitted verbatim so
    /// SQL / fenced code blocks render as intended (a per-turn heading separates turns).</summary>
    public string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.Append("# AI chat — ").AppendLine(string.IsNullOrEmpty(Title) ? "conversation" : Title);
        sb.Append("_Exported ").Append(UpdatedAt.ToString("u")).AppendLine("_").AppendLine();
        foreach (var turn in Turns)
        {
            sb.Append("## ").AppendLine(turn.Role == "user" ? "You" : "Assistant");
            sb.AppendLine(turn.Content).AppendLine();
        }
        return sb.ToString();
    }
}

/// <summary>
/// Spec 028 (M6) task T034 (US6). Persists the active AI chat conversation locally
/// (IndexedDB <see cref="StoreNames.ChatHistory"/>) so it survives a reload. Local-only — no
/// network egress, no sync (FR-033). Independent of the schema cache and key vault.
/// </summary>
public interface IChatHistoryStore
{
    /// <summary>The persisted conversation, or null when none is stored.</summary>
    Task<ChatConversation?> GetAsync();

    /// <summary>Persist (replace) the conversation. Stamps <see cref="ChatConversation.UpdatedAt"/>.</summary>
    Task SaveAsync(ChatConversation conversation);

    /// <summary>Drop the persisted conversation.</summary>
    Task ClearAsync();
}

internal sealed class ChatHistoryStore : IChatHistoryStore
{
    private const string Key = "current";
    private readonly IIndexedDbAdapter _store;

    public ChatHistoryStore(IIndexedDbAdapter store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<ChatConversation?> GetAsync()
    {
        var raw = await _store.GetAsync(StoreNames.ChatHistory, Key).ConfigureAwait(false);
        if (string.IsNullOrEmpty(raw)) return null;
        try { return JsonSerializer.Deserialize<ChatConversation>(raw!); }
        catch (JsonException) { return null; }
    }

    public Task SaveAsync(ChatConversation conversation)
    {
        if (conversation == null) throw new ArgumentNullException(nameof(conversation));
        conversation.UpdatedAt = DateTimeOffset.UtcNow;
        return _store.SetAsync(StoreNames.ChatHistory, Key, JsonSerializer.Serialize(conversation));
    }

    public Task ClearAsync() => _store.DeleteAsync(StoreNames.ChatHistory, Key);
}
