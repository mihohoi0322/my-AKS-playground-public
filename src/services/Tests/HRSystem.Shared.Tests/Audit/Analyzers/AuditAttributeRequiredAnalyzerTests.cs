using System.Collections.Immutable;
using System.Reflection;
using Grpc.Core;
using HRSystem.Audit.Analyzers;
using HRSystem.Shared.Audit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace HRSystem.Shared.Tests.Audit.Analyzers;

/// <summary>
/// Direct-compilation tests for <see cref="AuditAttributeRequiredAnalyzer"/>. We avoid
/// <c>Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.XUnit</c> because its 1.1.2 stable ships
/// .NETFramework binaries (NU1701 + Roslyn 1.0.1) and is incompatible with net10.0. Hosting the
/// analyzer inside a <see cref="CSharpCompilation"/> exercises the real symbol pipeline.
/// </summary>
public sealed class AuditAttributeRequiredAnalyzerTests
{
    [Fact]
    public async Task Reports_HRSAUD001_When_Override_Lacks_Annotation()
    {
        const string source = """
        using System.Threading.Tasks;
        using Grpc.Core;
        using HRSystem.Shared.Audit;

        namespace TestNs;

        public abstract class FakeBase
        {
            public virtual Task<string> Echo(string req, ServerCallContext ctx) => Task.FromResult(req);
        }

        public class FakeImpl : FakeBase
        {
            public override Task<string> Echo(string req, ServerCallContext ctx) => Task.FromResult(req);
        }
        """;

        var diagnostics = await GetAnalyzerDiagnosticsAsync(source);

        var hits = diagnostics
            .Where(d => d.Id == AuditAttributeRequiredAnalyzer.DiagnosticId)
            .ToList();

        Assert.Single(hits);
        Assert.Equal(DiagnosticSeverity.Error, hits[0].Severity);
        Assert.Contains("Echo", hits[0].GetMessage());
    }

    [Fact]
    public async Task DoesNotReport_When_Annotated_With_Audit()
    {
        const string source = """
        using System.Threading.Tasks;
        using Grpc.Core;
        using HRSystem.Shared.Audit;

        namespace TestNs;

        public abstract class FakeBase
        {
            public virtual Task<string> Echo(string req, ServerCallContext ctx) => Task.FromResult(req);
        }

        public class FakeImpl : FakeBase
        {
            [Audit(AuditEventType.EmployeeUpdated)]
            public override Task<string> Echo(string req, ServerCallContext ctx) => Task.FromResult(req);
        }
        """;

        var diagnostics = await GetAnalyzerDiagnosticsAsync(source);

        Assert.DoesNotContain(
            diagnostics,
            d => d.Id == AuditAttributeRequiredAnalyzer.DiagnosticId);
    }

    [Fact]
    public async Task DoesNotReport_When_Annotated_With_NoAudit()
    {
        const string source = """
        using System.Threading.Tasks;
        using Grpc.Core;
        using HRSystem.Shared.Audit;

        namespace TestNs;

        public abstract class FakeBase
        {
            public virtual Task<string> Echo(string req, ServerCallContext ctx) => Task.FromResult(req);
        }

        public class FakeImpl : FakeBase
        {
            [NoAudit("read-only")]
            public override Task<string> Echo(string req, ServerCallContext ctx) => Task.FromResult(req);
        }
        """;

        var diagnostics = await GetAnalyzerDiagnosticsAsync(source);

        Assert.DoesNotContain(
            diagnostics,
            d => d.Id == AuditAttributeRequiredAnalyzer.DiagnosticId);
    }

    [Fact]
    public async Task DoesNotReport_For_NonOverride_Methods_Even_With_ServerCallContext()
    {
        // Only public overrides participate in the rule (matches gRPC ServiceBase contract).
        const string source = """
        using System.Threading.Tasks;
        using Grpc.Core;

        namespace TestNs;

        public class HelperNotAnRpc
        {
            public Task<string> Helper(string req, ServerCallContext ctx) => Task.FromResult(req);
        }
        """;

        var diagnostics = await GetAnalyzerDiagnosticsAsync(source);

        Assert.DoesNotContain(
            diagnostics,
            d => d.Id == AuditAttributeRequiredAnalyzer.DiagnosticId);
    }

    private static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Task).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ServerCallContext).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(AuditAttribute).Assembly.Location),
        };

        // Pull in the trusted .NET runtime assemblies (System.Runtime, etc.) so the test sources
        // resolve types like System.Threading.Tasks.Task without manual reference plumbing.
        var trustedAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var path in trustedAssemblies)
        {
            // Skip duplicates we already added by-type.
            references.Add(MetadataReference.CreateFromFile(path));
        }

        var compilation = CSharpCompilation.Create(
            assemblyName: "AnalyzerTests",
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzer = new AuditAttributeRequiredAnalyzer();
        var withAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));
        return await withAnalyzers.GetAnalyzerDiagnosticsAsync();
    }
}
