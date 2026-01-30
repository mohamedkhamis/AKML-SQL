using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace AKML.SQL.Core.Services;

/// <summary>
/// Service for AI-powered SQL assistance.
/// Implements Story 10.1: AI Integration.
/// </summary>
public class AiService : IAiService
{
    private readonly ILogger<AiService> _logger;
    private readonly HttpClient _httpClient;
    private AiProviderSettings _settings;
    private readonly List<ConversationMessage> _conversationHistory;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public AiService(ILogger<AiService> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient();
        _settings = new AiProviderSettings();
        _conversationHistory = new List<ConversationMessage>();
    }

    /// <summary>
    /// Configures the AI provider settings.
    /// </summary>
    public void Configure(AiProviderSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _httpClient.DefaultRequestHeaders.Clear();

        if (!string.IsNullOrEmpty(settings.ApiKey))
        {
            switch (settings.Provider)
            {
                case AiProvider.OpenAI:
                    _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {settings.ApiKey}");
                    break;
                case AiProvider.Anthropic:
                    _httpClient.DefaultRequestHeaders.Add("x-api-key", settings.ApiKey);
                    _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
                    break;
                case AiProvider.AzureOpenAI:
                    _httpClient.DefaultRequestHeaders.Add("api-key", settings.ApiKey);
                    break;
            }
        }

        _logger.LogInformation("AI service configured with provider: {Provider}", settings.Provider);
    }

    /// <summary>
    /// Gets current provider settings.
    /// </summary>
    public AiProviderSettings GetSettings() => _settings;

    /// <summary>
    /// Converts natural language to SQL.
    /// </summary>
    public async Task<AiResponse> NaturalLanguageToSqlAsync(
        string naturalLanguage,
        AiSchemaContext? schemaContext = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(naturalLanguage))
            return AiResponse.Error("Natural language query cannot be empty");

        var systemPrompt = BuildNl2SqlSystemPrompt(schemaContext);
        var userPrompt = $"Convert to SQL: {naturalLanguage}";

        return await SendRequestAsync(systemPrompt, userPrompt, cancellationToken);
    }

    /// <summary>
    /// Explains what a SQL query does.
    /// </summary>
    public async Task<AiResponse> ExplainSqlAsync(
        string sql,
        ExplanationLevel level = ExplanationLevel.Detailed,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return AiResponse.Error("SQL query cannot be empty");

        var systemPrompt = BuildExplainSystemPrompt(level);
        var userPrompt = $"Explain this SQL query:\n\n```sql\n{sql}\n```";

        return await SendRequestAsync(systemPrompt, userPrompt, cancellationToken);
    }

    /// <summary>
    /// Suggests optimizations for a SQL query.
    /// </summary>
    public async Task<AiResponse> SuggestOptimizationsAsync(
        string sql,
        AiSchemaContext? schemaContext = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return AiResponse.Error("SQL query cannot be empty");

        var systemPrompt = BuildOptimizationSystemPrompt(schemaContext);
        var userPrompt = $"Analyze and suggest optimizations for this SQL query:\n\n```sql\n{sql}\n```";

        return await SendRequestAsync(systemPrompt, userPrompt, cancellationToken);
    }

    /// <summary>
    /// Fixes errors in a SQL query.
    /// </summary>
    public async Task<AiResponse> FixSqlErrorAsync(
        string sql,
        string errorMessage,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return AiResponse.Error("SQL query cannot be empty");

        if (string.IsNullOrWhiteSpace(errorMessage))
            return AiResponse.Error("Error message cannot be empty");

        var systemPrompt = BuildFixErrorSystemPrompt();
        var userPrompt = $"Fix this SQL query that has an error:\n\nSQL:\n```sql\n{sql}\n```\n\nError: {errorMessage}";

        return await SendRequestAsync(systemPrompt, userPrompt, cancellationToken);
    }

    /// <summary>
    /// Generates SQL from a template with placeholders.
    /// </summary>
    public async Task<AiResponse> GenerateSqlAsync(
        string template,
        Dictionary<string, string>? parameters = null,
        AiSchemaContext? schemaContext = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(template))
            return AiResponse.Error("Template cannot be empty");

        var systemPrompt = BuildGenerateSystemPrompt(schemaContext);
        var sb = new StringBuilder($"Generate SQL based on this template: {template}");

        if (parameters != null && parameters.Count > 0)
        {
            sb.AppendLine("\n\nParameters:");
            foreach (var param in parameters)
            {
                sb.AppendLine($"- {param.Key}: {param.Value}");
            }
        }

        return await SendRequestAsync(systemPrompt, sb.ToString(), cancellationToken);
    }

    /// <summary>
    /// Continues a conversation about SQL.
    /// </summary>
    public async Task<AiResponse> ChatAsync(
        string message,
        bool maintainContext = true,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message))
            return AiResponse.Error("Message cannot be empty");

        var systemPrompt = BuildChatSystemPrompt();

        if (maintainContext)
        {
            _conversationHistory.Add(new ConversationMessage { Role = "user", Content = message });
        }

        var response = await SendRequestWithHistoryAsync(
            systemPrompt,
            maintainContext ? _conversationHistory : new List<ConversationMessage> { new() { Role = "user", Content = message } },
            cancellationToken);

        if (maintainContext && response.Success)
        {
            _conversationHistory.Add(new ConversationMessage { Role = "assistant", Content = response.Content });
        }

        return response;
    }

    /// <summary>
    /// Clears conversation history.
    /// </summary>
    public void ClearConversation()
    {
        _conversationHistory.Clear();
        _logger.LogDebug("Conversation history cleared");
    }

    /// <summary>
    /// Validates if the AI service is properly configured.
    /// </summary>
    public bool IsConfigured()
    {
        return !string.IsNullOrEmpty(_settings.ApiKey) &&
               !string.IsNullOrEmpty(_settings.Endpoint);
    }

    /// <summary>
    /// Tests the connection to the AI provider.
    /// </summary>
    public async Task<AiResponse> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured())
            return AiResponse.Error("AI service is not configured. Please provide API key and endpoint.");

        try
        {
            var response = await SendRequestAsync(
                "You are a helpful assistant. Respond with 'Connection successful' only.",
                "Test",
                cancellationToken);

            return response.Success
                ? AiResponse.Ok("Connection test successful")
                : response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Connection test failed");
            return AiResponse.Error($"Connection test failed: {ex.Message}");
        }
    }

    #region Private Methods

    private async Task<AiResponse> SendRequestAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        var messages = new List<ConversationMessage>
        {
            new() { Role = "user", Content = userPrompt }
        };

        return await SendRequestWithHistoryAsync(systemPrompt, messages, cancellationToken);
    }

    private async Task<AiResponse> SendRequestWithHistoryAsync(
        string systemPrompt,
        List<ConversationMessage> messages,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured())
            return AiResponse.Error("AI service is not configured");

        try
        {
            object requestBody = _settings.Provider switch
            {
                AiProvider.OpenAI or AiProvider.AzureOpenAI => BuildOpenAiRequest(systemPrompt, messages),
                AiProvider.Anthropic => BuildAnthropicRequest(systemPrompt, messages),
                _ => throw new NotSupportedException($"Provider {_settings.Provider} is not supported")
            };

            var json = JsonSerializer.Serialize(requestBody, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(_settings.Endpoint, content, cancellationToken);
            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("AI request failed: {StatusCode} - {Response}", response.StatusCode, responseJson);
                return AiResponse.Error($"AI request failed: {response.StatusCode}");
            }

            var result = ParseResponse(responseJson);
            return AiResponse.Ok(result);
        }
        catch (TaskCanceledException)
        {
            return AiResponse.Error("Request was cancelled");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP request failed");
            return AiResponse.Error($"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI request failed");
            return AiResponse.Error($"Error: {ex.Message}");
        }
    }

    private object BuildOpenAiRequest(string systemPrompt, List<ConversationMessage> messages)
    {
        var allMessages = new List<object>
        {
            new { role = "system", content = systemPrompt }
        };

        allMessages.AddRange(messages.Select(m => new { role = m.Role, content = m.Content }));

        return new
        {
            model = _settings.Model,
            messages = allMessages,
            max_tokens = _settings.MaxTokens,
            temperature = _settings.Temperature
        };
    }

    private object BuildAnthropicRequest(string systemPrompt, List<ConversationMessage> messages)
    {
        return new
        {
            model = _settings.Model,
            system = systemPrompt,
            messages = messages.Select(m => new { role = m.Role, content = m.Content }),
            max_tokens = _settings.MaxTokens,
            temperature = _settings.Temperature
        };
    }

    private string ParseResponse(string responseJson)
    {
        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        // OpenAI format
        if (root.TryGetProperty("choices", out var choices))
        {
            return choices[0].GetProperty("message").GetProperty("content").GetString() ?? "";
        }

        // Anthropic format
        if (root.TryGetProperty("content", out var content))
        {
            return content[0].GetProperty("text").GetString() ?? "";
        }

        throw new InvalidOperationException("Unknown response format");
    }

    private static string BuildNl2SqlSystemPrompt(AiSchemaContext? schema)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a SQL Server expert. Convert natural language queries to T-SQL.");
        sb.AppendLine("Rules:");
        sb.AppendLine("- Output ONLY the SQL query, no explanations");
        sb.AppendLine("- Use T-SQL syntax for SQL Server");
        sb.AppendLine("- Use proper quoting for identifiers when needed");
        sb.AppendLine("- Include comments for complex logic");

        if (schema != null)
        {
            sb.AppendLine("\nAvailable schema:");
            AppendSchemaContext(sb, schema);
        }

        return sb.ToString();
    }

    private static string BuildExplainSystemPrompt(ExplanationLevel level)
    {
        var detail = level switch
        {
            ExplanationLevel.Brief => "Provide a one-sentence summary.",
            ExplanationLevel.Detailed => "Explain step by step what the query does, including joins, filters, and aggregations.",
            ExplanationLevel.Educational => "Explain in detail for someone learning SQL. Include why each clause is used and SQL concepts involved.",
            _ => "Explain what the query does."
        };

        return $"You are a SQL expert helping users understand queries. {detail}";
    }

    private static string BuildOptimizationSystemPrompt(AiSchemaContext? schema)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a SQL Server performance expert. Analyze queries and suggest optimizations.");
        sb.AppendLine("Consider:");
        sb.AppendLine("- Index usage and suggestions");
        sb.AppendLine("- Query structure improvements");
        sb.AppendLine("- Common anti-patterns");
        sb.AppendLine("- Execution plan considerations");
        sb.AppendLine("\nProvide specific, actionable suggestions with examples.");

        if (schema != null)
        {
            sb.AppendLine("\nSchema context:");
            AppendSchemaContext(sb, schema);
        }

        return sb.ToString();
    }

    private static string BuildFixErrorSystemPrompt()
    {
        return @"You are a SQL Server expert helping fix query errors.
Analyze the error message and SQL query, then:
1. Identify the cause of the error
2. Provide the corrected SQL query
3. Briefly explain what was wrong

Format your response as:
**Corrected SQL:**
```sql
[corrected query]
```

**Explanation:**
[brief explanation]";
    }

    private static string BuildGenerateSystemPrompt(AiSchemaContext? schema)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a SQL Server expert. Generate T-SQL queries based on templates and parameters.");
        sb.AppendLine("Output ONLY the SQL query, properly formatted.");

        if (schema != null)
        {
            sb.AppendLine("\nAvailable schema:");
            AppendSchemaContext(sb, schema);
        }

        return sb.ToString();
    }

    private static string BuildChatSystemPrompt()
    {
        return @"You are an expert SQL Server assistant. Help users with:
- Writing SQL queries
- Explaining SQL concepts
- Debugging query issues
- Performance optimization
- Database design questions

Be concise but thorough. Use T-SQL syntax for SQL Server.";
    }

    private static void AppendSchemaContext(StringBuilder sb, AiSchemaContext schema)
    {
        if (schema.Tables?.Count > 0)
        {
            sb.AppendLine("Tables:");
            foreach (var table in schema.Tables)
            {
                sb.AppendLine($"  - {table.Schema}.{table.Name}");
                if (table.Columns?.Count > 0)
                {
                    foreach (var col in table.Columns.Take(10))
                    {
                        sb.AppendLine($"      {col.Name} ({col.DataType})");
                    }
                    if (table.Columns.Count > 10)
                        sb.AppendLine($"      ... and {table.Columns.Count - 10} more columns");
                }
            }
        }
    }

    #endregion
}

/// <summary>
/// Interface for AI service.
/// </summary>
public interface IAiService
{
    void Configure(AiProviderSettings settings);
    AiProviderSettings GetSettings();
    bool IsConfigured();
    Task<AiResponse> TestConnectionAsync(CancellationToken cancellationToken = default);
    Task<AiResponse> NaturalLanguageToSqlAsync(string naturalLanguage, AiSchemaContext? schemaContext = null, CancellationToken cancellationToken = default);
    Task<AiResponse> ExplainSqlAsync(string sql, ExplanationLevel level = ExplanationLevel.Detailed, CancellationToken cancellationToken = default);
    Task<AiResponse> SuggestOptimizationsAsync(string sql, AiSchemaContext? schemaContext = null, CancellationToken cancellationToken = default);
    Task<AiResponse> FixSqlErrorAsync(string sql, string errorMessage, CancellationToken cancellationToken = default);
    Task<AiResponse> GenerateSqlAsync(string template, Dictionary<string, string>? parameters = null, AiSchemaContext? schemaContext = null, CancellationToken cancellationToken = default);
    Task<AiResponse> ChatAsync(string message, bool maintainContext = true, CancellationToken cancellationToken = default);
    void ClearConversation();
}

/// <summary>
/// AI provider configuration.
/// </summary>
public class AiProviderSettings
{
    public AiProvider Provider { get; set; } = AiProvider.OpenAI;
    public string ApiKey { get; set; } = string.Empty;
    public string Endpoint { get; set; } = "https://api.openai.com/v1/chat/completions";
    public string Model { get; set; } = "gpt-4o-mini";
    public int MaxTokens { get; set; } = 2048;
    public double Temperature { get; set; } = 0.3;
}

/// <summary>
/// Supported AI providers.
/// </summary>
public enum AiProvider
{
    OpenAI,
    Anthropic,
    AzureOpenAI
}

/// <summary>
/// AI response wrapper.
/// </summary>
public class AiResponse
{
    public bool Success { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }

    public static AiResponse Ok(string content) => new() { Success = true, Content = content };
    public static AiResponse Error(string message) => new() { Success = false, ErrorMessage = message };
}

/// <summary>
/// Explanation detail level.
/// </summary>
public enum ExplanationLevel
{
    Brief,
    Detailed,
    Educational
}

/// <summary>
/// Schema context for AI queries.
/// </summary>
public class AiSchemaContext
{
    public List<AiTableInfo>? Tables { get; set; }
    public string? DatabaseName { get; set; }
}

/// <summary>
/// Table information for AI schema context.
/// </summary>
public class AiTableInfo
{
    public string Schema { get; set; } = "dbo";
    public string Name { get; set; } = string.Empty;
    public List<AiColumnInfo>? Columns { get; set; }
}

/// <summary>
/// Column information for AI schema context.
/// </summary>
public class AiColumnInfo
{
    public string Name { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public bool IsNullable { get; set; }
    public bool IsPrimaryKey { get; set; }
}

/// <summary>
/// Conversation message for chat history.
/// </summary>
internal class ConversationMessage
{
    public string Role { get; set; } = "user";
    public string Content { get; set; } = string.Empty;
}
