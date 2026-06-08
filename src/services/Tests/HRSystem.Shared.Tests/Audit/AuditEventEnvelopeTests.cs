using System.Text.Json;
using CloudNative.CloudEvents;
using HRSystem.Shared.Audit;

namespace HRSystem.Shared.Tests.Audit;

public sealed class AuditEventEnvelopeTests
{
    private sealed record DelegationSummary(string GrantorObjectId, string GranteeObjectId, string Scope) : IAuditPayload;

    private static AuditAmbient BuildAmbient() => new(
        Actor: new AuditActor("00000000-0000-0000-0000-000000000001", "user"),
        ActingAs: new AuditActor("00000000-0000-0000-0000-000000000002", "user"),
        DelegationPolicySnapshot: new DelegationPolicySnapshot("v1", "abc123"),
        ClientIpHash: "sha256:deadbeef",
        UserAgent: "test-agent/1.0",
        Traceparent: "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01");

    private static AuditEventDescriptor<DelegationSummary> BuildDescriptor() => new(
        Type: AuditEventType.DelegationCreated,
        ResourceType: "delegation",
        ResourceId: "deleg-42",
        Action: AuditAction.Grant,
        Result: AuditResult.Success,
        Classification: AuditClassification.MutationHigh,
        BeforeSummary: null,
        AfterSummary: new DelegationSummary("user-a", "user-b", "ApprovalScope"));

    [Fact]
    public void Build_PopulatesCloudEventsRequiredAttributes()
    {
        var ce = AuditEventEnvelope.Build(
            BuildDescriptor(),
            BuildAmbient(),
            source: "/hrsystem/delegation-service/Grant");

        Assert.Equal(CloudEventsSpecVersion.V1_0, ce.SpecVersion);
        Assert.Equal(AuditEventType.DelegationCreated, ce.Type);
        Assert.Equal("delegation/deleg-42", ce.Subject);
        Assert.Equal("/hrsystem/delegation-service/Grant", ce.Source!.ToString());
        Assert.Equal("application/json", ce.DataContentType);
        Assert.NotNull(ce.DataSchema);
        Assert.StartsWith("https://schemas.hrsystem.local/audit/", ce.DataSchema!.ToString());
        Assert.NotNull(ce.Time);
        Assert.NotNull(ce.Id);
        Assert.NotEmpty(ce.Id!);
    }

    [Fact]
    public void Build_OverridesTimeWithServerClock_WhenNotSupplied()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var ce = AuditEventEnvelope.Build(
            BuildDescriptor(),
            BuildAmbient(),
            source: "/hrsystem/test");
        var after = DateTimeOffset.UtcNow.AddSeconds(1);

        Assert.NotNull(ce.Time);
        Assert.InRange(ce.Time!.Value, before, after);
    }

    [Fact]
    public void SerializeAndDeserialize_StructuredMode_RoundTripsAllAttributes()
    {
        var original = AuditEventEnvelope.Build(
            BuildDescriptor(),
            BuildAmbient(),
            source: "/hrsystem/delegation-service/Grant",
            id: "11111111-1111-1111-1111-111111111111",
            time: new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero));

        var bytes = AuditEventEnvelope.SerializeStructured(original);
        var roundTripped = AuditEventEnvelope.DeserializeStructured(bytes);

        Assert.Equal(original.Id, roundTripped.Id);
        Assert.Equal(original.Type, roundTripped.Type);
        Assert.Equal(original.Subject, roundTripped.Subject);
        Assert.Equal(original.Source, roundTripped.Source);
        Assert.Equal(original.Time, roundTripped.Time);
        Assert.Equal(original.DataSchema, roundTripped.DataSchema);
        Assert.Equal(original.SpecVersion, roundTripped.SpecVersion);
    }

    [Fact]
    public void SerializeStructured_EmitsActorAndActingAsAndDelegationSnapshot()
    {
        var ce = AuditEventEnvelope.Build(
            BuildDescriptor(),
            BuildAmbient(),
            source: "/hrsystem/delegation-service/Grant");

        var json = AuditEventEnvelope.SerializeStructured(ce);
        using var doc = JsonDocument.Parse(json);

        // Structured mode wraps payload under "data".
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal("00000000-0000-0000-0000-000000000001", data.GetProperty("actor").GetProperty("objectId").GetString());
        Assert.Equal("00000000-0000-0000-0000-000000000002", data.GetProperty("actingAs").GetProperty("objectId").GetString());
        Assert.Equal("v1", data.GetProperty("delegationPolicySnapshot").GetProperty("version").GetString());
        Assert.Equal("sha256:deadbeef", data.GetProperty("clientIpHash").GetString());
    }

    [Fact]
    public void BuildDataSchemaUri_AppendsTypeAndVersion()
    {
        var uri = AuditEventEnvelope.BuildDataSchemaUri("hrsystem.delegation.granted.v1");
        Assert.Equal("https://schemas.hrsystem.local/audit/hrsystem.delegation.granted/v1.json", uri);
    }

    [Fact]
    public void Build_RejectsNullDescriptorAmbientOrSource()
    {
        Assert.Throws<ArgumentNullException>(() =>
            AuditEventEnvelope.Build<DelegationSummary>(null!, BuildAmbient(), "/x"));
        Assert.Throws<ArgumentNullException>(() =>
            AuditEventEnvelope.Build(BuildDescriptor(), null!, "/x"));
        Assert.Throws<ArgumentException>(() =>
            AuditEventEnvelope.Build(BuildDescriptor(), BuildAmbient(), "  "));
    }
}
