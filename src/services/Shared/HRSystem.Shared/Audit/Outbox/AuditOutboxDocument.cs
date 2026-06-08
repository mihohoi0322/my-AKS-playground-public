using Newtonsoft.Json;
using NewtonsoftConverters = Newtonsoft.Json.Converters;
using STJ = System.Text.Json.Serialization;

namespace HRSystem.Shared.Audit.Outbox;

/// <summary>
/// Status field on an outbox document. The worker advances pending → shipping → shipped using
/// ETag optimistic concurrency. On failure the worker either resets to <see cref="Pending"/>
/// or increments a retry counter (depending on the failure mode); see
/// <see cref="AuditOutboxWorker"/>.
/// </summary>
public enum AuditOutboxStatus
{
    /// <summary>Default state for newly inserted hot-index documents.</summary>
    Pending,

    /// <summary>The worker has claimed this document and is shipping it to Append Blob.</summary>
    Shipping,

    /// <summary>Shipping completed; safe to be expired by Cosmos TTL (90 days).</summary>
    Shipped,
}

/// <summary>
/// Minimal projection of an <c>auditHotIndex</c> document used by the Change Feed Processor
/// handler.
///
/// Decorated with both Newtonsoft and System.Text.Json attributes: the Cosmos SDK 3.x default
/// serializer is Newtonsoft-based, but the W2-B contract requires an STJ-compatible model so
/// the W3 ingestion path (which uses STJ end-to-end) can reuse the same DTO.
/// </summary>
public sealed class AuditOutboxDocument
{
    [JsonProperty("id")]
    [STJ.JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Hierarchical PK component 1.</summary>
    [JsonProperty("eventDate")]
    [STJ.JsonPropertyName("eventDate")]
    public string EventDate { get; set; } = string.Empty;

    /// <summary>Hierarchical PK component 2.</summary>
    [JsonProperty("actorObjectId")]
    [STJ.JsonPropertyName("actorObjectId")]
    public string ActorObjectId { get; set; } = string.Empty;

    /// <summary>Outbox status. Worker advances this via ETag-checked replace.</summary>
    [JsonProperty("status")]
    [JsonConverter(typeof(NewtonsoftConverters.StringEnumConverter))]
    [STJ.JsonPropertyName("status")]
    [STJ.JsonConverter(typeof(STJ.JsonStringEnumConverter<AuditOutboxStatus>))]
    public AuditOutboxStatus Status { get; set; } = AuditOutboxStatus.Pending;

    /// <summary>
    /// Number of failed shipping attempts. Increments when the worker observes a transient
    /// failure between the pending→shipping transition and the shipped→shipping transition.
    /// </summary>
    [JsonProperty("retryCount")]
    [STJ.JsonPropertyName("retryCount")]
    public int RetryCount { get; set; }

    /// <summary>CloudEvents <c>id</c>, used for Append Blob idempotency.</summary>
    [JsonProperty("auditId")]
    [STJ.JsonPropertyName("auditId")]
    public string? AuditId { get; set; }

    /// <summary>CloudEvents structured-mode JSON. Opaque to the worker; forwarded verbatim.</summary>
    [JsonProperty("envelope")]
    [STJ.JsonPropertyName("envelope")]
    public string? Envelope { get; set; }

    /// <summary>Cosmos system property, ETag for optimistic concurrency.</summary>
    [JsonProperty("_etag")]
    [STJ.JsonPropertyName("_etag")]
    public string? ETag { get; set; }

    /// <summary>Cosmos system property, server timestamp (epoch seconds).</summary>
    [JsonProperty("_ts")]
    [STJ.JsonPropertyName("_ts")]
    public long Ts { get; set; }
}
