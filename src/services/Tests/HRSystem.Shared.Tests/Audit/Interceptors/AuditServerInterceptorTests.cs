using Grpc.Core;
using HRSystem.Shared.Audit;
using HRSystem.Shared.Audit.Interceptors;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRSystem.Shared.Tests.Audit.Interceptors;

public sealed class AuditServerInterceptorTests
{
    [Fact]
    public async Task Unary_PushesAndRestoresAmbient_Symmetrically()
    {
        await Task.Run(async () =>
        {
            Assert.Null(AmbientAuditContext.Current);

            var registry = BuildRegistryFor(nameof(StubService.AuditedRpc), audited: true);
            var interceptor = NewInterceptor(registry);

            var headers = new Metadata
            {
                { AuditHeaders.ActorOid, "alice-oid" },
                { AuditHeaders.ActorType, "user" },
                { AuditHeaders.ActingAs, "boss-oid" },
            };
            var ctx = new TestServerCallContext($"/HRSystem.Tests/{nameof(StubService.AuditedRpc)}", headers);

            string? observedActor = null;
            string? observedActingAs = null;
            var response = await interceptor.UnaryServerHandler<string, string>(
                "req",
                ctx,
                (req, c) =>
                {
                    observedActor = AmbientAuditContext.Current?.Actor.ObjectId;
                    observedActingAs = AmbientAuditContext.Current?.ActingAs?.ObjectId;
                    return Task.FromResult("ok");
                });

            Assert.Equal("ok", response);
            Assert.Equal("alice-oid", observedActor);
            Assert.Equal("boss-oid", observedActingAs);
            Assert.Null(AmbientAuditContext.Current);
        });
    }

    [Fact]
    public async Task Unary_RestoresAmbient_EvenOnException()
    {
        await Task.Run(async () =>
        {
            Assert.Null(AmbientAuditContext.Current);

            var registry = BuildRegistryFor(nameof(StubService.AuditedRpc), audited: true);
            var interceptor = NewInterceptor(registry);
            var ctx = new TestServerCallContext($"/HRSystem.Tests/{nameof(StubService.AuditedRpc)}");

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                interceptor.UnaryServerHandler<string, string>(
                    "req",
                    ctx,
                    (_, _) => throw new InvalidOperationException("boom")));

            Assert.Null(AmbientAuditContext.Current);
        });
    }

    [Fact]
    public async Task Skipped_NoAuditMethod_DoesNotInvokeWriter()
    {
        var registry = BuildRegistryFor(nameof(StubService.SkippedRpc), audited: false);
        var writer = new RecordingAuditWriter();
        var interceptor = new AuditServerInterceptor(writer, registry, NullLogger<AuditServerInterceptor>.Instance);
        var ctx = new TestServerCallContext($"/HRSystem.Tests/{nameof(StubService.SkippedRpc)}");

        var response = await interceptor.UnaryServerHandler<string, string>(
            "req",
            ctx,
            (_, _) => Task.FromResult("ok"));

        Assert.Equal("ok", response);
        Assert.Equal(0, writer.WriteCount);
    }

    [Fact]
    public async Task Audited_Method_InvokesWriterOnce()
    {
        var registry = BuildRegistryFor(nameof(StubService.AuditedRpc), audited: true);
        var writer = new RecordingAuditWriter();
        var interceptor = new AuditServerInterceptor(writer, registry, NullLogger<AuditServerInterceptor>.Instance);
        var ctx = new TestServerCallContext($"/HRSystem.Tests/{nameof(StubService.AuditedRpc)}");

        await interceptor.UnaryServerHandler<string, string>(
            "req",
            ctx,
            (_, _) => Task.FromResult("ok"));

        Assert.Equal(1, writer.WriteCount);
        Assert.Equal(AuditEventType.EmployeeUpdated, writer.LastType);
    }

    [Fact]
    public void Registry_IsBuiltOnce_AndSubsequentCallsDoNotReReflect()
    {
        var registry = BuildRegistryFor(nameof(StubService.AuditedRpc), audited: true);
        var initial = registry.ReflectionResolutionCount;
        Assert.True(initial > 0);

        // Repeated lookups must not re-invoke reflection.
        for (var i = 0; i < 10; i++)
        {
            Assert.True(registry.TryGet($"/HRSystem.Tests/{nameof(StubService.AuditedRpc)}", out _));
        }

        Assert.Equal(initial, registry.ReflectionResolutionCount);
    }

    [Fact]
    public async Task UnknownPath_Throws()
    {
        var registry = BuildRegistryFor(nameof(StubService.AuditedRpc), audited: true);
        var interceptor = NewInterceptor(registry);
        var ctx = new TestServerCallContext("/Unknown/Method");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            interceptor.UnaryServerHandler<string, string>(
                "req",
                ctx,
                (_, _) => Task.FromResult("ok")));
    }

    private static AuditServerInterceptor NewInterceptor(AuditMethodRegistry registry)
    {
        return new AuditServerInterceptor(
            new NoopAuditWriter(),
            registry,
            NullLogger<AuditServerInterceptor>.Instance);
    }

    private static AuditMethodRegistry BuildRegistryFor(string methodName, bool audited)
    {
        var method = typeof(StubService).GetMethod(methodName)!;
        AuditAttribute? audit = audited ? new AuditAttribute(AuditEventType.EmployeeUpdated) : null;
        NoAuditAttribute? noAudit = audited ? null : new NoAuditAttribute("read-only");
        var meta = new AuditMethodMetadata(
            GrpcPath: $"/HRSystem.Tests/{methodName}",
            GrpcServiceName: "HRSystem.Tests",
            MethodName: methodName,
            Method: method,
            Audit: audit,
            NoAudit: noAudit);
        return new AuditMethodRegistry(new[] { meta });
    }

    // ----- Helpers -----------------------------------------------------------

    public class StubService
    {
        public Task<string> AuditedRpc(string req, ServerCallContext ctx) => Task.FromResult(req);
        public Task<string> SkippedRpc(string req, ServerCallContext ctx) => Task.FromResult(req);
    }

    private sealed class RecordingAuditWriter : IAuditWriter
    {
        public int WriteCount { get; private set; }
        public string? LastType { get; private set; }

        public Task WriteAsync<TPayload>(
            AuditEventDescriptor<TPayload> descriptor,
            CancellationToken cancellationToken) where TPayload : class, IAuditPayload
        {
            WriteCount++;
            LastType = descriptor.Type;
            return Task.CompletedTask;
        }
    }
}
