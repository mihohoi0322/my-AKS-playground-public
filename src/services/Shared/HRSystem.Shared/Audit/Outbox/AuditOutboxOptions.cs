using System.ComponentModel.DataAnnotations;

namespace HRSystem.Shared.Audit.Outbox;

/// <summary>
/// Configuration for <see cref="AuditOutboxWorker"/>.
///
/// The worker uses Cosmos DB Change Feed Processor (latest-version mode) to project new
/// audit-outbox documents from <see cref="SourceContainerName"/> into Append Blob WORM storage.
/// Lease state is managed in <see cref="LeaseContainerName"/> by the SDK; nothing in
/// HRSystem.Shared writes to leases directly.
///
/// The Bicep / db-design changes that provision the lease container are tracked separately
/// (W2-E follow-up F-5). This type only references the names.
/// </summary>
public sealed class AuditOutboxOptions
{
    /// <summary>Configuration section name (<c>AuditOutbox</c>).</summary>
    public const string SectionName = "AuditOutbox";

    /// <summary>
    /// Cosmos container holding the hot audit index. Source of the change feed.
    /// Hierarchical PK: <c>/eventDate</c>, <c>/actorObjectId</c> (docs/db-design.md).
    /// </summary>
    [Required]
    public string SourceContainerName { get; set; } = "auditHotIndex";

    /// <summary>Cosmos container that stores the SDK-managed lease state.</summary>
    [Required]
    public string LeaseContainerName { get; set; } = "auditLease";

    /// <summary>
    /// Change Feed Processor poll interval. Lower = lower lag, higher RU.
    /// 5 seconds keeps p99 lag well under the 5-minute SLO (docs/features/audit-log.md).
    /// </summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Lease renew interval. Must be smaller than the SDK default lease expiration (60s).
    /// Set to 17s so a missed renew leaves &gt;3 chances before expiration.
    /// </summary>
    public TimeSpan LeaseRenewInterval { get; set; } = TimeSpan.FromSeconds(17);

    /// <summary>
    /// Stable instance name advertised to the Change Feed Processor for partition assignment.
    /// Defaults to the host name; in K8s this is the Pod name (Downward API).
    /// </summary>
    public string InstanceName { get; set; } = Environment.MachineName;

    /// <summary>
    /// Logical processor name. All worker replicas must share this so they cooperatively
    /// distribute partitions; changing it after deployment will replay from beginning.
    /// </summary>
    public string ProcessorName { get; set; } = "audit-outbox";

    /// <summary>
    /// Max documents per change feed batch. The SDK default is 100; we keep the same default
    /// but make it tunable for backpressure tests.
    /// </summary>
    [Range(1, 1000)]
    public int MaxItemsPerBatch { get; set; } = 100;

    /// <summary>
    /// When <c>true</c> the worker is registered but the Change Feed Processor is not started
    /// at host startup. Useful for tests and for Pods where outbox shipping is intentionally
    /// disabled (e.g. read-only replicas).
    /// </summary>
    public bool Enabled { get; set; } = true;
}
