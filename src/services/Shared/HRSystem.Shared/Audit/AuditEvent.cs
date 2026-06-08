using System.Text.Json;
using System.Text.Json.Serialization;
using CloudNative.CloudEvents;
using CloudNative.CloudEvents.SystemTextJson;

namespace HRSystem.Shared.Audit;

/// <summary>
/// CloudEvents 1.0 envelope for HRSystem audit events. Provides a System.Text.Json-based
/// round-trip helper used by writers and tests. <c>Newtonsoft.Json</c> is intentionally not used
/// (docs/features/audit-log.md §CloudEvents schema versioning strategy).
/// </summary>
public static class AuditEventEnvelope
{
    /// <summary>Always <c>1.0</c> for Phase 1.</summary>
    public const string SpecVersion = "1.0";

    /// <summary>Schema registry base URL (Phase 1: read-only Storage Blob).</summary>
    public const string SchemaRegistryBase = "https://schemas.hrsystem.local/audit";

    /// <summary>Default media type for the data payload.</summary>
    public const string DataContentType = "application/json";

    /// <summary>
    /// Compose a CloudEvent (1.0) from a descriptor + ambient context. <c>id</c> defaults to a
    /// fresh GUID and <c>time</c> is always overwritten with <see cref="DateTimeOffset.UtcNow"/>
    /// on the server side. Phase 1 uses GUID; ULID adoption is a follow-up.
    /// </summary>
    public static CloudEvent Build<TPayload>(
        AuditEventDescriptor<TPayload> descriptor,
        AuditAmbient ambient,
        string source,
        string? id = null,
        DateTimeOffset? time = null)
        where TPayload : class, IAuditPayload
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(ambient);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        var ce = new CloudEvent
        {
            Id = id ?? Guid.NewGuid().ToString("D"),
            Source = new Uri(source, UriKind.RelativeOrAbsolute),
            Type = descriptor.Type,
            Subject = $"{descriptor.ResourceType}/{descriptor.ResourceId}",
            Time = time ?? DateTimeOffset.UtcNow,
            DataSchema = new Uri(BuildDataSchemaUri(descriptor.Type), UriKind.Absolute),
            DataContentType = DataContentType,
            Data = new AuditData<TPayload>
            {
                Action = descriptor.Action,
                Result = descriptor.Result,
                Classification = descriptor.Classification,
                Actor = ambient.Actor,
                ActingAs = ambient.ActingAs,
                DelegationPolicySnapshot = ambient.DelegationPolicySnapshot,
                ClientIpHash = ambient.ClientIpHash,
                UserAgent = ambient.UserAgent,
                Traceparent = ambient.Traceparent,
                BeforeSummary = descriptor.BeforeSummary,
                AfterSummary = descriptor.AfterSummary,
            },
        };
        return ce;
    }

    /// <summary>
    /// Serialize a CloudEvent to its 1.0 JSON structured-mode encoding.
    /// </summary>
    public static byte[] SerializeStructured(CloudEvent cloudEvent, JsonSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(cloudEvent);
        var formatter = new JsonEventFormatter(options ?? DefaultJsonOptions, documentOptions: default);
        var bytes = formatter.EncodeStructuredModeMessage(cloudEvent, out _);
        return bytes.ToArray();
    }

    /// <summary>
    /// Deserialize a CloudEvent from its 1.0 JSON structured-mode encoding.
    /// </summary>
    public static CloudEvent DeserializeStructured(ReadOnlyMemory<byte> bytes, JsonSerializerOptions? options = null)
    {
        var formatter = new JsonEventFormatter(options ?? DefaultJsonOptions, documentOptions: default);
        return formatter.DecodeStructuredModeMessage(bytes, contentType: null, extensionAttributes: null);
    }

    /// <summary>
    /// Build the <c>dataschema</c> URI for a given event <c>type</c>.
    /// Convention: <c>{registry}/{type-without-version}/{version}.json</c>.
    /// </summary>
    public static string BuildDataSchemaUri(string type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        var lastDot = type.LastIndexOf('.');
        if (lastDot < 0 || lastDot == type.Length - 1)
        {
            return $"{SchemaRegistryBase}/{type}/v1.json";
        }
        var typeWithoutVersion = type[..lastDot];
        var version = type[(lastDot + 1)..];
        return $"{SchemaRegistryBase}/{typeWithoutVersion}/{version}.json";
    }

    /// <summary>
    /// JsonSerializerOptions shared across audit serialization (snake_case-ish via property
    /// naming on records; minimal allocation).
    /// </summary>
    public static readonly JsonSerializerOptions DefaultJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };
}

/// <summary>
/// Strongly-typed shape of the CloudEvents <c>data</c> payload for HRSystem audit events.
/// Mirrors the required attribute set from docs/features/audit-log.md.
/// </summary>
internal sealed class AuditData<TPayload>
    where TPayload : class, IAuditPayload
{
    [JsonPropertyName("action")] public AuditAction Action { get; init; }
    [JsonPropertyName("result")] public AuditResult Result { get; init; }
    [JsonPropertyName("classification")] public AuditClassification Classification { get; init; }
    [JsonPropertyName("actor")] public AuditActor Actor { get; init; } = null!;
    [JsonPropertyName("actingAs")] public AuditActor? ActingAs { get; init; }
    [JsonPropertyName("delegationPolicySnapshot")] public DelegationPolicySnapshot? DelegationPolicySnapshot { get; init; }
    [JsonPropertyName("clientIpHash")] public string? ClientIpHash { get; init; }
    [JsonPropertyName("userAgent")] public string? UserAgent { get; init; }
    [JsonPropertyName("traceparent")] public string? Traceparent { get; init; }
    [JsonPropertyName("beforeSummary")] public TPayload? BeforeSummary { get; init; }
    [JsonPropertyName("afterSummary")] public TPayload? AfterSummary { get; init; }
}
