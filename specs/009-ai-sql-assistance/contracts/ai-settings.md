# Settings Contract: AI-Powered SQL Assistance

**Date**: 2026-03-25
**Location**: `%AppData%/AKML SQL/config.json` under `"ai"` key

## Configuration Schema

```json
{
  "ai": {
    "enabled": false,
    "provider": "",
    "model": "",
    "apiKey": "",
    "endpoint": "",
    "maxTokens": 4096,
    "temperature": 0.2,
    "timeout": 30,
    "retries": 2,
    "privacyMode": "schemaOnly",
    "offlineProvider": "",
    "offlineModel": "",
    "offlineEndpoint": "",
    "textToSql": true,
    "explain": true,
    "fix": true,
    "autoFixOnError": false,
    "optimize": true,
    "indexSuggestions": true,
    "inlineCompletion": false,
    "chatPanel": true
  }
}
```

## Field Definitions

| Field | Type | Default | Valid Values | Description |
|-------|------|---------|--------------|-------------|
| `enabled` | bool | `false` | true/false | Master switch. All AI features disabled when false. |
| `provider` | string | `""` | `"anthropic"`, `"openai"`, `"azure"`, `"gemini"`, `"ollama"`, `"lmstudio"`, `"custom"`, `""` | Active AI provider. Empty = not configured. |
| `model` | string | `""` | Provider-specific model ID | e.g., `"claude-sonnet-4-20250514"`, `"gpt-4o"`, `"codellama:13b"` |
| `apiKey` | string | `""` | `""` or `"dpapi:..."` | DPAPI-encrypted API key. Stored as `"dpapi:"` + base64(encrypted blob). |
| `endpoint` | string | `""` | Valid URL or `""` | Custom endpoint. Required for Azure, Ollama, LM Studio, custom. |
| `maxTokens` | int | `4096` | 1–100000 | Maximum tokens per AI response. |
| `temperature` | double | `0.2` | 0.0–2.0 | AI creativity level. Lower = more deterministic. |
| `timeout` | int | `30` | 5–120 | Request timeout in seconds. |
| `retries` | int | `2` | 0–5 | Retry count on transient failures. |
| `privacyMode` | string | `"schemaOnly"` | `"full"`, `"schemaOnly"`, `"anonymous"`, `"offline"`, `"disabled"` | Controls what data is transmitted. |
| `offlineProvider` | string | `""` | Same as `provider` | Fallback provider when cloud is unavailable. |
| `offlineModel` | string | `""` | Provider-specific model ID | Fallback model. |
| `offlineEndpoint` | string | `""` | Valid URL or `""` | Fallback endpoint. |
| `textToSql` | bool | `true` | true/false | Enable text-to-SQL generation. |
| `explain` | bool | `true` | true/false | Enable AI Explain. |
| `fix` | bool | `true` | true/false | Enable AI Fix. |
| `autoFixOnError` | bool | `false` | true/false | Auto-offer fix on query failure. |
| `optimize` | bool | `true` | true/false | Enable AI Optimize. |
| `indexSuggestions` | bool | `true` | true/false | Enable AI index analysis. |
| `inlineCompletion` | bool | `false` | true/false | Enable ghost text predictions (opt-in). |
| `chatPanel` | bool | `true` | true/false | Enable AI chat panel. |

## Privacy Mode Behaviors

| Mode | Schema Names Sent | Query Sent | Data Values Sent | Network |
|------|-------------------|------------|------------------|---------|
| `full` | Yes (real) | Yes (real) | Yes (real) | Cloud |
| `schemaOnly` | Yes (real) | Yes (redacted literals) | No | Cloud |
| `anonymous` | Yes (hashed) | Yes (redacted + hashed) | No | Cloud |
| `offline` | N/A | N/A | N/A | Local only |
| `disabled` | N/A | N/A | N/A | None |

## Provider-Specific Requirements

| Provider | Requires `apiKey` | Requires `endpoint` | Notes |
|----------|-------------------|---------------------|-------|
| `anthropic` | Yes | No | Uses default Anthropic API endpoint |
| `openai` | Yes | No | Uses default OpenAI API endpoint |
| `azure` | Yes | Yes | Endpoint = Azure resource URL |
| `gemini` | Yes | No | Uses default Google AI Studio endpoint |
| `ollama` | No | Yes (default `http://localhost:11434`) | Local model, no API key needed |
| `lmstudio` | No | Yes (default `http://localhost:1234/v1`) | Local model, no API key needed |
| `custom` | Optional | Yes | Any OpenAI-compatible endpoint |

## Keyboard Shortcuts

| Shortcut | Command | Configurable |
|----------|---------|--------------|
| `Ctrl+Shift+G` | Text-to-SQL | Yes |
| `Ctrl+Shift+E` | AI Explain | Yes |
| `Shift+Alt+R` | AI Fix | Yes |
| `Ctrl+Shift+O` | AI Optimize | Yes |
| `Ctrl+Shift+A` | AI Chat Panel | Yes |
