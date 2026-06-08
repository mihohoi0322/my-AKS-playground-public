namespace HRSystem.Shared.Audit;

/// <summary>
/// Marks a property as containing personally identifiable information.
/// Phase 1 defines the attribute only; the source generator that emits redact methods
/// is delivered in a later step (see docs/features/audit-log.md §PII).
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
public sealed class PiiAttribute : Attribute
{
    /// <summary>
    /// Hint used by the future source generator to pick a redaction strategy
    /// (e.g. <c>"hash"</c>, <c>"mask"</c>, <c>"drop"</c>). Optional.
    /// </summary>
    public string? Strategy { get; init; }
}

/// <summary>
/// Marks a property as security-sensitive (credentials, tokens, internal IDs that should
/// never appear in audit summaries). The future source generator drops sensitive fields
/// from <c>beforeSummary</c> / <c>afterSummary</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
public sealed class SensitiveAttribute : Attribute
{
}
