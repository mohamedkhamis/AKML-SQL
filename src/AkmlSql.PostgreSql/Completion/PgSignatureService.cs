using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;
using AkmlSql.PostgreSql.Reference;
using Serilog;

namespace AkmlSql.PostgreSql.Completion;

/// <summary>
/// Spec 042 (task T102, FR-062, scenario US3-6) - signature help inside a call: the forms of a built-in function from the
/// reference, or the overloads of a user-defined function or procedure from the schema, with the argument the caret is in.
/// </summary>
public sealed class PgSignatureService
{
    private readonly PgReference _reference;

    public PgSignatureService() : this(PgReference.Instance) { }

    public PgSignatureService(PgReference reference) => _reference = reference;

    /// <summary>A response with no overloads when the caret is not inside a call, or the function is not known.</summary>
    public SignatureResponse Get(string? text, int caret, DatabaseCache? cache, int serverMajor = 18)
    {
        try
        {
            var context = PgCursorContextAnalyzer.Analyze(text, caret);
            if (context.Suppressed || context.CallName == null) return None;

            var view = new PgCatalogView(cache, new PgCompletionOptions { IncludeSystemObjects = true });
            var overloads = UserOverloads(context, view);
            if (overloads.Count == 0) overloads = BuiltInOverloads(context, _reference.For(serverMajor));
            if (overloads.Count == 0) return None;

            return new SignatureResponse
            {
                FunctionName = context.CallName,
                Overloads = overloads.ToArray(),
                ActiveOverload = Best(overloads, context.CallArgument),
                ActiveParameter = context.CallArgument,
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "PostgreSQL signature help failed at offset {Offset}", caret);
            return None;
        }
    }

    private static SignatureResponse None => new() { FunctionName = string.Empty, Overloads = Array.Empty<SignatureOverload>() };

    private static List<SignatureOverload> UserOverloads(PgCursorContext c, PgCatalogView view)
    {
        var result = new List<SignatureOverload>();
        foreach (var o in view.VisibleObjects())
        {
            if (o.ObjectType is not (DbObjectType.Procedure or DbObjectType.ScalarFunction or DbObjectType.TableFunction or DbObjectType.InlineFunction)) continue;
            if (!string.Equals(o.ObjectName, c.CallName, StringComparison.Ordinal)) continue;
            if (c.CallSchema != null ? !string.Equals(o.SchemaName, c.CallSchema, StringComparison.Ordinal) : !view.InSearchPath(o.SchemaName)) continue;

            var parameters = o.Parameters.Where(p => !p.IsOutput).Select(p => new ParameterInfo
            {
                Name = p.ParameterName,
                Type = p.TypeName,
                Documentation = string.Empty,
                IsOptional = p.HasDefault,
            }).ToArray();

            result.Add(new SignatureOverload
            {
                Label = $"{o.ObjectName}({string.Join(", ", parameters.Select(p => string.IsNullOrEmpty(p.Name) ? p.Type : p.Name + " " + p.Type))})",
                Documentation = string.IsNullOrWhiteSpace(o.Description) ? (o.ObjectType == DbObjectType.Procedure ? "procedure " : "function ") + o.FullName : o.Description!,
                Parameters = parameters,
            });
        }
        return result;
    }

    private static List<SignatureOverload> BuiltInOverloads(PgCursorContext c, PgReference reference)
    {
        var result = new List<SignatureOverload>();
        foreach (var entry in reference.Callables(c.CallName!))
        {
            foreach (var s in entry.Signatures)
            {
                var parameters = s.Args.Where(a => a.Mode != "out").Select(a => new ParameterInfo
                {
                    Name = a.Name ?? string.Empty,
                    Type = a.Type,
                    Documentation = a.Mode == "variadic" ? "repeats" : string.Empty,
                    IsOptional = a.HasDefault || a.Mode == "variadic",
                }).ToArray();

                var returns = s.Returns is { Length: > 0 } r ? " -> " + (s.IsSetReturning ? "setof " : string.Empty) + r : string.Empty;
                result.Add(new SignatureOverload
                {
                    Label = $"{entry.Name}({string.Join(", ", s.Args.Where(a => a.Mode != "out").Select(Label))}){returns}",
                    Documentation = entry.Summary,
                    Parameters = parameters,
                });
            }
        }
        return result;
    }

    private static string Label(PgReferenceArg a) =>
        (string.IsNullOrEmpty(a.Name) ? string.Empty : a.Name + " ") + a.Type + (a.Mode == "variadic" ? ", ..." : string.Empty);

    // the first form with a parameter at the active position; a variadic one covers any position
    private static int Best(IReadOnlyList<SignatureOverload> overloads, int argument)
    {
        for (var i = 0; i < overloads.Count; i++)
            if (overloads[i].Parameters.Length > argument) return i;
        for (var i = 0; i < overloads.Count; i++)
            if (overloads[i].Parameters.Length > 0 && overloads[i].Parameters[^1].Documentation == "repeats") return i;
        return 0;
    }
}
