using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace HRSystem.Audit.Analyzers;

/// <summary>
/// HRSAUD001: Every gRPC service method (a public override that takes a
/// <c>Grpc.Core.ServerCallContext</c> parameter) must be annotated with either
/// <c>[Audit(eventType)]</c> or <c>[NoAudit("reason")]</c>. Build error.
/// </summary>
/// <remarks>
/// Defence-in-depth pair with <c>AuditAttributeValidator</c> (startup reflection scan):
/// the analyzer fails the build before review, the validator fails Pod start-up if the
/// analyzer was somehow bypassed (binary drop-in, suppression, etc.).
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AuditAttributeRequiredAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "HRSAUD001";

    private const string Title = "gRPC RPC method missing [Audit] or [NoAudit]";
    private const string MessageFormat =
        "gRPC service method '{0}' must be annotated with [Audit] or [NoAudit(\"reason\")]";
    private const string Description =
        "Every public override that takes a Grpc.Core.ServerCallContext parameter is an audit-relevant entry point " +
        "and must declare its audit posture. Use [Audit(AuditEventType.X)] for write paths and " +
        "[NoAudit(\"reason\")] for read-only queries.";
    private const string Category = "Audit";

    private const string ServerCallContextFullName = "Grpc.Core.ServerCallContext";
    private const string AuditAttributeFullName = "HRSystem.Shared.Audit.AuditAttribute";
    private const string NoAuditAttributeFullName = "HRSystem.Shared.Audit.NoAuditAttribute";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: Title,
        messageFormat: MessageFormat,
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: Description);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeMethod, SymbolKind.Method);
    }

    private static void AnalyzeMethod(SymbolAnalysisContext context)
    {
        var method = (IMethodSymbol)context.Symbol;

        if (method.MethodKind != MethodKind.Ordinary)
        {
            return;
        }
        if (method.DeclaredAccessibility != Accessibility.Public)
        {
            return;
        }
        if (!method.IsOverride)
        {
            return;
        }

        var hasServerCallContext = method.Parameters.Any(p =>
            string.Equals(p.Type.ToDisplayString(), ServerCallContextFullName, System.StringComparison.Ordinal));
        if (!hasServerCallContext)
        {
            return;
        }

        var hasAudit = method.GetAttributes().Any(a =>
            string.Equals(a.AttributeClass?.ToDisplayString(), AuditAttributeFullName, System.StringComparison.Ordinal));
        var hasNoAudit = method.GetAttributes().Any(a =>
            string.Equals(a.AttributeClass?.ToDisplayString(), NoAuditAttributeFullName, System.StringComparison.Ordinal));

        if (hasAudit || hasNoAudit)
        {
            return;
        }

        var location = method.Locations.FirstOrDefault() ?? Location.None;

        // Prefer the identifier on the syntax node when available for clearer squiggles.
        if (method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is MethodDeclarationSyntax syntax)
        {
            location = syntax.Identifier.GetLocation();
        }

        var diagnostic = Diagnostic.Create(Rule, location, method.Name);
        context.ReportDiagnostic(diagnostic);
    }
}
