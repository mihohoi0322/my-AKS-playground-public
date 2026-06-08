using HRSystem.Shared.Audit.Outbox;
using Newtonsoft.Json;

namespace HRSystem.Shared.Tests.Audit.Outbox;

/// <summary>
/// Round-trip tests for <see cref="AuditOutboxDocument"/> against the Cosmos SDK 3.x default
/// Newtonsoft serializer. These assertions guard the system property names (<c>_etag</c>,
/// <c>_ts</c>) and the hierarchical PK fields against accidental rename.
/// </summary>
public sealed class AuditOutboxDocumentTests
{
    [Fact]
    public void NewtonsoftSerialization_PreservesSystemPropertyNames()
    {
        var doc = new AuditOutboxDocument
        {
            Id = "evt-1",
            EventDate = "2026-04-26",
            ActorObjectId = "00000000-0000-0000-0000-000000000001",
            Status = AuditOutboxStatus.Pending,
            RetryCount = 2,
            AuditId = "evt-1",
            Envelope = "{\"type\":\"x\"}",
            ETag = "\"abc\"",
            Ts = 1745654321,
        };

        var json = JsonConvert.SerializeObject(doc);

        Assert.Contains("\"id\":\"evt-1\"", json);
        Assert.Contains("\"eventDate\":\"2026-04-26\"", json);
        Assert.Contains("\"actorObjectId\":\"00000000-0000-0000-0000-000000000001\"", json);
        Assert.Contains("\"status\":\"Pending\"", json);
        Assert.Contains("\"retryCount\":2", json);
        Assert.Contains("\"_etag\":", json);
        Assert.Contains("\"_ts\":1745654321", json);
    }

    [Fact]
    public void NewtonsoftDeserialization_ReadsStatusAndSystemFields()
    {
        const string json = """
            {
              "id": "evt-2",
              "eventDate": "2026-04-26",
              "actorObjectId": "actor",
              "status": "Shipping",
              "retryCount": 0,
              "auditId": "evt-2",
              "envelope": "{}",
              "_etag": "\"xyz\"",
              "_ts": 1745654400
            }
            """;
        var doc = JsonConvert.DeserializeObject<AuditOutboxDocument>(json)!;
        Assert.Equal(AuditOutboxStatus.Shipping, doc.Status);
        Assert.Equal("\"xyz\"", doc.ETag);
        Assert.Equal(1745654400, doc.Ts);
    }
}
