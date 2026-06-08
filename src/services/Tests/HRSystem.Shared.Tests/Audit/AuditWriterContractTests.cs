using HRSystem.Shared.Audit;

namespace HRSystem.Shared.Tests.Audit;

/// <summary>
/// Interface-level contract tests for <see cref="IAuditWriter"/>. The actual fail-closed
/// Cosmos-backed implementation is delivered in W3; here we only assert the contract surface
/// and the no-op stub's invariants.
/// </summary>
public sealed class AuditWriterContractTests
{
    private sealed record EmptyPayload : IAuditPayload;

    private static AuditEventDescriptor<EmptyPayload> BuildDescriptor() => new(
        Type: AuditEventType.AuditViewAttempt,
        ResourceType: "audit",
        ResourceId: "any",
        Action: AuditAction.Read,
        Result: AuditResult.Success,
        Classification: AuditClassification.ReadHigh,
        BeforeSummary: null,
        AfterSummary: null);

    [Fact]
    public void IAuditWriter_HasOnlyWriteAsyncMethod()
    {
        // Contract guard: the interface intentionally has a single method. Adding members
        // requires a follow-up review (T-09 attribution invariants depend on this surface).
        var methods = typeof(IAuditWriter).GetMethods();
        Assert.Single(methods);
        Assert.Equal(nameof(IAuditWriter.WriteAsync), methods[0].Name);
    }

    [Fact]
    public void WriteAsync_DoesNotAcceptActorOrActingAsArgument()
    {
        // Per docs/features/audit-log.md: actor / actingAs MUST be sourced from
        // AmbientAuditContext, never from caller-supplied parameters (T-09).
        var method = typeof(IAuditWriter).GetMethod(nameof(IAuditWriter.WriteAsync))!;
        var paramNames = method.GetParameters().Select(p => p.Name).ToArray();
        Assert.DoesNotContain("actor", paramNames);
        Assert.DoesNotContain("actingAs", paramNames);
    }

    [Fact]
    public async Task NoopAuditWriter_AcceptsValidDescriptor_AndCompletes()
    {
        IAuditWriter writer = new NoopAuditWriter();
        await writer.WriteAsync(BuildDescriptor(), CancellationToken.None);
    }

    [Fact]
    public async Task NoopAuditWriter_RejectsNullDescriptor()
    {
        IAuditWriter writer = new NoopAuditWriter();
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            writer.WriteAsync<EmptyPayload>(null!, CancellationToken.None));
    }

    [Fact]
    public async Task NoopAuditWriter_HonoursCancellation()
    {
        IAuditWriter writer = new NoopAuditWriter();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            writer.WriteAsync(BuildDescriptor(), cts.Token));
    }
}
