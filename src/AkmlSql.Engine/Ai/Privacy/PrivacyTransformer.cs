using AkmlSql.Core.Models.Ai;
using AkmlSql.Engine.Parser;
using Serilog;

namespace AkmlSql.Engine.Ai.Privacy;

/// <summary>
/// Orchestrates the privacy transformation pipeline applied to SQL and schema context
/// before sending them to an AI provider.
/// <para>
/// Behavior depends on the configured privacy mode:
/// <list type="bullet">
///   <item><c>"full"</c>: SQL and schema context are sent unchanged.</item>
///   <item><c>"schemaOnly"</c>: Literal values in SQL are redacted; schema context is sent with real names.</item>
///   <item><c>"anonymous"</c>: Literals are redacted and all identifiers (tables, columns, schemas) are
///     hashed in both the SQL and schema context.</item>
///   <item><c>"offline"</c> / <c>"disabled"</c>: AI features are not available; throws
///     <see cref="InvalidOperationException"/>.</item>
/// </list>
/// </para>
/// </summary>
public sealed class PrivacyTransformer : IDisposable
{
    private readonly LiteralRedactor _literalRedactor;
    private IdentifierHasher? _identifierHasher;

    /// <summary>
    /// Initializes a new <see cref="PrivacyTransformer"/> with the shared parser service.
    /// </summary>
    /// <param name="parser">
    /// The T-SQL parser service used by <see cref="LiteralRedactor"/> for AST-based redaction.
    /// </param>
    public PrivacyTransformer(TsqlParserService parser)
    {
        ArgumentNullException.ThrowIfNull(parser);
        _literalRedactor = new LiteralRedactor(parser);
    }

    /// <summary>
    /// Transforms SQL and schema context according to the specified privacy mode.
    /// </summary>
    /// <param name="sql">The raw SQL text to transform.</param>
    /// <param name="context">The schema context (may be null if not available).</param>
    /// <param name="privacyMode">
    /// The privacy mode: <c>"full"</c>, <c>"schemaOnly"</c>, <c>"anonymous"</c>,
    /// <c>"offline"</c>, or <c>"disabled"</c>.
    /// </param>
    /// <returns>
    /// A tuple of the transformed SQL, transformed schema context, and the
    /// <see cref="PrivacyTransformation"/> record needed to reverse the transformation.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="privacyMode"/> is <c>"offline"</c> or <c>"disabled"</c>.
    /// </exception>
    public (string transformedSql, SchemaContext? transformedContext, PrivacyTransformation transformation)
        Transform(string sql, SchemaContext? context, string privacyMode)
    {
        ArgumentNullException.ThrowIfNull(sql);
        ArgumentNullException.ThrowIfNull(privacyMode);

        var mode = privacyMode.Trim().ToLowerInvariant();

        return mode switch
        {
            "full" => TransformFull(sql, context),
            "schemaonly" => TransformSchemaOnly(sql, context),
            "anonymous" => TransformAnonymous(sql, context),
            "offline" or "disabled" => throw new InvalidOperationException(
                $"AI features are not available in {privacyMode} privacy mode"),
            _ => throw new ArgumentException($"Unknown privacy mode: {privacyMode}", nameof(privacyMode))
        };
    }

    /// <summary>
    /// Reverses a privacy transformation on an AI response, restoring original identifiers
    /// and literal references where applicable.
    /// </summary>
    /// <param name="aiResponse">The AI-generated response text to de-transform.</param>
    /// <param name="transformation">
    /// The <see cref="PrivacyTransformation"/> returned from the corresponding
    /// <see cref="Transform"/> call.
    /// </param>
    /// <returns>The response text with hashed identifiers and literal placeholders restored.</returns>
    public static string DeTransform(string aiResponse, PrivacyTransformation transformation)
    {
        if (string.IsNullOrEmpty(aiResponse))
            return aiResponse;

        ArgumentNullException.ThrowIfNull(transformation);

        var result = aiResponse;

        // Step 1: Reverse identifier hashing (anonymous mode)
        if (transformation.IdentifierMap.Count > 0)
        {
            // Replace longer hashed identifiers first to avoid partial matches
            foreach (var (hashed, original) in transformation.IdentifierMap
                         .OrderByDescending(kvp => kvp.Key.Length))
            {
                result = result.Replace(hashed, original, StringComparison.Ordinal);
            }
        }

        // Step 2: Reverse literal placeholders (if AI referenced them in its response)
        if (transformation.LiteralMap.Count > 0)
        {
            foreach (var (placeholder, original) in transformation.LiteralMap)
            {
                // Replace placeholder references the AI may have echoed back
                // Handle both with and without surrounding quotes
                result = result.Replace($"'{placeholder}'", $"'{original}'", StringComparison.Ordinal);
                result = result.Replace(placeholder, original, StringComparison.Ordinal);
            }
        }

        return result;
    }

    /// <summary>Full mode: no transformation applied.</summary>
    private static (string, SchemaContext?, PrivacyTransformation) TransformFull(
        string sql, SchemaContext? context)
    {
        Log.Debug("PrivacyTransformer: full mode — no transformation applied");

        return (sql, context, new PrivacyTransformation { Mode = "full" });
    }

    /// <summary>SchemaOnly mode: redact literals in SQL, keep schema context with real names.</summary>
    private (string, SchemaContext?, PrivacyTransformation) TransformSchemaOnly(
        string sql, SchemaContext? context)
    {
        Log.Debug("PrivacyTransformer: schemaOnly mode — redacting literals");

        var (redactedSql, literalMap) = _literalRedactor.Redact(sql);

        var transformation = new PrivacyTransformation
        {
            Mode = "schemaOnly",
            LiteralMap = literalMap
        };

        return (redactedSql, context, transformation);
    }

    /// <summary>
    /// Anonymous mode: redact literals and hash all identifiers in both SQL and schema context.
    /// </summary>
    private (string, SchemaContext?, PrivacyTransformation) TransformAnonymous(
        string sql, SchemaContext? context)
    {
        Log.Debug("PrivacyTransformer: anonymous mode — redacting literals and hashing identifiers");

        // Step 1: Redact literals
        var (redactedSql, literalMap) = _literalRedactor.Redact(sql);

        // Step 2: Create (or reuse) identifier hasher for this session
        _identifierHasher ??= new IdentifierHasher();

        // Step 3: Hash schema context first (this populates the forward/reverse maps)
        SchemaContext? hashedContext = null;
        if (context != null)
        {
            hashedContext = _identifierHasher.HashSchema(context);
        }

        // Step 4: Hash identifiers in the redacted SQL
        var hashedSql = _identifierHasher.HashSql(redactedSql);

        var transformation = new PrivacyTransformation
        {
            Mode = "anonymous",
            LiteralMap = literalMap,
            IdentifierMap = new Dictionary<string, string>(_identifierHasher.ReverseMap)
        };

        return (hashedSql, hashedContext, transformation);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _identifierHasher?.Dispose();
    }
}
