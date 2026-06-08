using System.Diagnostics;
using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HRSystem.Shared.Audit.Outbox;

/// <summary>
/// Background service that drains the audit outbox by tailing the Cosmos
/// <c>auditHotIndex</c> container's change feed (latest-version mode) and shipping each
/// document to Append Blob WORM via <see cref="IAuditWriter"/>.
///
/// Per-document lifecycle:
/// <list type="number">
/// <item>CFP delivers a batch of changed documents to the handler.</item>
/// <item>For each document the worker advances <c>pending → shipping</c> via ReplaceItemAsync
///   with <c>IfMatchEtag</c> (optimistic concurrency).</item>
/// <item>On success the worker invokes <see cref="IAuditWriter.WriteAsync{TPayload}"/>
///   (currently the W1 <see cref="NoopAuditWriter"/> stub; W3 swaps in the Append Blob
///   writer).</item>
/// <item>On success the document is replaced again, this time <c>shipping → shipped</c>.</item>
/// <item>On any failure the worker either resets to <c>pending</c> (so the next CFP delivery
///   retries) or, if the failure is a stale ETag (<c>412 PreconditionFailed</c>), assumes a
///   peer worker beat us and skips the document.</item>
/// </list>
///
/// W3 follow-ups: real Append Blob shipping; <c>ChangeFeedEstimator</c> for an authoritative
/// depth metric; transactional batch write of business doc + outbox doc; verifier CronJob.
/// </summary>
public sealed class AuditOutboxWorker : BackgroundService
{
    private readonly IAuditOutboxCosmosClientProvider _clientProvider;
    private readonly IAuditWriter _auditWriter;
    private readonly AuditOutboxOptions _options;
    private readonly ILogger<AuditOutboxWorker> _logger;
    private ChangeFeedProcessor? _processor;
    private Container? _sourceContainer;

    public AuditOutboxWorker(
        IAuditOutboxCosmosClientProvider clientProvider,
        IAuditWriter auditWriter,
        IOptions<AuditOutboxOptions> options,
        ILogger<AuditOutboxWorker> logger)
    {
        _clientProvider = clientProvider ?? throw new ArgumentNullException(nameof(clientProvider));
        _auditWriter = auditWriter ?? throw new ArgumentNullException(nameof(auditWriter));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("AuditOutboxWorker is disabled (AuditOutbox:Enabled=false). Skipping.");
            return;
        }

        try
        {
            _processor = BuildProcessor();
            await _processor.StartAsync().ConfigureAwait(false);
            _logger.LogInformation(
                "AuditOutboxWorker started: source={Source} lease={Lease} instance={Instance} poll={Poll}s",
                _options.SourceContainerName,
                _options.LeaseContainerName,
                _options.InstanceName,
                _options.PollingInterval.TotalSeconds);

            // Block until the host requests shutdown. Throws OperationCanceledException
            // when stoppingToken is signalled, which is the expected exit path.
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            // Failing to even start the CFP is fatal for audit completeness; bubble up so the
            // host treats this as an unhealthy state. Do not swallow.
            AuditMetrics.OutboxErrorsTotal.Add(1,
                new KeyValuePair<string, object?>("phase", "startup"),
                new KeyValuePair<string, object?>("instance", _options.InstanceName));
            _logger.LogError(ex, "AuditOutboxWorker failed to start the Change Feed Processor.");
            throw;
        }
        finally
        {
            if (_processor is not null)
            {
                try
                {
                    await _processor.StopAsync().ConfigureAwait(false);
                    _logger.LogInformation("AuditOutboxWorker stopped.");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error while stopping the Change Feed Processor.");
                }
            }
        }
    }

    private ChangeFeedProcessor BuildProcessor()
    {
        var client = _clientProvider.GetClient();
        var database = client.GetDatabase(_clientProvider.DatabaseName);
        _sourceContainer = database.GetContainer(_options.SourceContainerName);
        var leaseContainer = database.GetContainer(_options.LeaseContainerName);

        return _sourceContainer
            .GetChangeFeedProcessorBuilder<AuditOutboxDocument>(_options.ProcessorName, HandleChangesAsync)
            .WithInstanceName(_options.InstanceName)
            .WithLeaseContainer(leaseContainer)
            .WithMaxItems(_options.MaxItemsPerBatch)
            .WithPollInterval(_options.PollingInterval)
            .WithLeaseConfiguration(renewInterval: _options.LeaseRenewInterval)
            .WithErrorNotification(HandleErrorAsync)
            .Build();
    }

    /// <summary>
    /// Per-batch handler delivered by the Change Feed Processor. Updates lag/depth metrics
    /// then ships each document individually so a single bad doc doesn't poison the batch.
    /// </summary>
    internal async Task HandleChangesAsync(
        ChangeFeedProcessorContext context,
        IReadOnlyCollection<AuditOutboxDocument> changes,
        CancellationToken cancellationToken)
    {
        if (changes.Count == 0) return;

        UpdateBatchMetrics(changes);

        foreach (var doc in changes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ShipOneAsync(doc, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Error notification sink for the Change Feed Processor.</summary>
    internal Task HandleErrorAsync(string leaseToken, Exception exception)
    {
        AuditMetrics.OutboxErrorsTotal.Add(1,
            new KeyValuePair<string, object?>("phase", "cfp"),
            new KeyValuePair<string, object?>("instance", _options.InstanceName));
        _logger.LogError(
            exception,
            "Change Feed Processor reported an error on lease {LeaseToken} (instance={Instance}).",
            leaseToken,
            _options.InstanceName);
        return Task.CompletedTask;
    }

    private void UpdateBatchMetrics(IReadOnlyCollection<AuditOutboxDocument> changes)
    {
        var nowEpochSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long minTs = long.MaxValue;
        long pending = 0;
        foreach (var d in changes)
        {
            if (d.Ts > 0 && d.Ts < minTs) minTs = d.Ts;
            if (d.Status == AuditOutboxStatus.Pending) pending++;
        }
        if (minTs != long.MaxValue)
        {
            var lagSeconds = Math.Max(0, nowEpochSeconds - minTs);
            AuditMetrics.OutboxLagSeconds.Record(
                lagSeconds,
                new KeyValuePair<string, object?>("instance", _options.InstanceName));
        }
        AuditMetrics.SetOutboxDepth(pending);
    }

    private async Task ShipOneAsync(AuditOutboxDocument doc, CancellationToken cancellationToken)
    {
        if (_sourceContainer is null) return;
        if (doc.Status == AuditOutboxStatus.Shipped) return; // already done; ignore
        if (string.IsNullOrEmpty(doc.Id) || string.IsNullOrEmpty(doc.EventDate))
        {
            AuditMetrics.OutboxErrorsTotal.Add(1,
                new KeyValuePair<string, object?>("phase", "validation"),
                new KeyValuePair<string, object?>("instance", _options.InstanceName));
            _logger.LogWarning(
                "Skipping outbox document with missing id/eventDate (id={Id}, eventDate={EventDate}).",
                doc.Id,
                doc.EventDate);
            return;
        }

        var partitionKey = new PartitionKeyBuilder()
            .Add(doc.EventDate)
            .Add(doc.ActorObjectId ?? string.Empty)
            .Build();

        var sw = Stopwatch.StartNew();
        var startingEtag = doc.ETag;
        AuditOutboxDocument? claimed;
        try
        {
            claimed = await TryTransitionAsync(
                doc,
                AuditOutboxStatus.Pending,
                AuditOutboxStatus.Shipping,
                partitionKey,
                cancellationToken).ConfigureAwait(false);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            // Stale ETag — a peer worker (or the previous incarnation of this worker) already
            // claimed the document. This is expected under multi-replica operation.
            _logger.LogDebug(
                "Outbox doc {Id} skipped: stale ETag (peer worker likely owns it).",
                doc.Id);
            return;
        }

        if (claimed is null)
        {
            // Document was not in pending state — either already shipping or shipped.
            return;
        }

        try
        {
            // W3: replace NoopAuditWriter with the real Append Blob writer. For the skeleton
            // we only exercise the writer plumbing; the descriptor is intentionally minimal.
            var descriptor = new AuditEventDescriptor<AuditOutboxEnvelopePayload>(
                Type: AuditEventType.AuditViewAttempt, // placeholder; W3 will derive from envelope
                ResourceType: "audit",
                ResourceId: claimed.AuditId ?? claimed.Id,
                Action: AuditAction.Read,
                Result: AuditResult.Success,
                Classification: AuditClassification.ReadHigh,
                BeforeSummary: null,
                AfterSummary: new AuditOutboxEnvelopePayload(claimed.Envelope));
            await _auditWriter.WriteAsync(descriptor, cancellationToken).ConfigureAwait(false);

            var shipped = await TryTransitionAsync(
                claimed,
                AuditOutboxStatus.Shipping,
                AuditOutboxStatus.Shipped,
                partitionKey,
                cancellationToken).ConfigureAwait(false);

            if (shipped is not null)
            {
                AuditMetrics.OutboxProcessedTotal.Add(1,
                    new KeyValuePair<string, object?>("instance", _options.InstanceName));
            }
        }
        catch (Exception ex)
        {
            AuditMetrics.OutboxErrorsTotal.Add(1,
                new KeyValuePair<string, object?>("phase", "shipping"),
                new KeyValuePair<string, object?>("instance", _options.InstanceName));
            _logger.LogWarning(
                ex,
                "Outbox shipping failed for doc {Id}; resetting to pending for retry (retryCount={Retry}, originalEtag={Etag}).",
                claimed.Id,
                claimed.RetryCount,
                startingEtag);
            try
            {
                claimed.RetryCount += 1;
                await TryTransitionAsync(
                    claimed,
                    AuditOutboxStatus.Shipping,
                    AuditOutboxStatus.Pending,
                    partitionKey,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception resetEx)
            {
                _logger.LogError(
                    resetEx,
                    "Failed to reset outbox doc {Id} from shipping back to pending. Manual reconciliation required.",
                    claimed.Id);
            }
        }
        finally
        {
            sw.Stop();
            AuditMetrics.OutboxShippingDurationMs.Record(
                sw.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("instance", _options.InstanceName));
        }
    }

    /// <summary>
    /// Replace the document with <c>newStatus</c> only if its current status equals
    /// <c>expectedStatus</c> AND the ETag matches. Returns the replaced document with the
    /// fresh ETag, or <c>null</c> if the precondition (status mismatch) was not met.
    /// CosmosException is propagated so callers can distinguish ETag conflicts.
    /// </summary>
    private async Task<AuditOutboxDocument?> TryTransitionAsync(
        AuditOutboxDocument doc,
        AuditOutboxStatus expectedStatus,
        AuditOutboxStatus newStatus,
        PartitionKey partitionKey,
        CancellationToken cancellationToken)
    {
        if (_sourceContainer is null) throw new InvalidOperationException("Source container not initialised.");
        if (doc.Status != expectedStatus) return null;

        doc.Status = newStatus;
        var requestOptions = new ItemRequestOptions
        {
            IfMatchEtag = doc.ETag,
            EnableContentResponseOnWrite = true,
        };
        var response = await _sourceContainer.ReplaceItemAsync(
            doc,
            doc.Id,
            partitionKey,
            requestOptions,
            cancellationToken).ConfigureAwait(false);
        return response.Resource;
    }

    /// <summary>Wrapper payload so the CloudEvents envelope satisfies <see cref="IAuditPayload"/>.</summary>
    internal sealed record AuditOutboxEnvelopePayload(string? Envelope) : IAuditPayload;
}
