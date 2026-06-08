namespace HRSystem.Shared.Audit;

/// <summary>
/// gRPC metadata header names used by the api-gateway to propagate the authenticated actor and
/// optional acting-as principal into business services. The interceptor reads these headers
/// to construct the <see cref="AmbientAuditContext"/> at the request boundary.
/// </summary>
/// <remarks>
/// Header names are intentionally lower-case (gRPC metadata is case-insensitive but stored lowercased).
/// The <c>x-hrsystem-acting-as</c> value is treated as a stub in W2; W3 replaces it with a
/// server-side ApprovalService lookup so clients cannot forge delegation.
/// </remarks>
public static class AuditHeaders
{
    /// <summary>Stable identifier of the authenticated principal (e.g. Entra ID object id).</summary>
    public const string ActorOid = "x-hrsystem-actor-oid";

    /// <summary>Actor kind (defaults to <c>"user"</c> when absent).</summary>
    public const string ActorType = "x-hrsystem-actor-type";

    /// <summary>Optional actingAs object id; W3 will override with server-side lookup.</summary>
    public const string ActingAs = "x-hrsystem-acting-as";

    /// <summary>Optional actingAs actor kind (defaults to <c>"user"</c>).</summary>
    public const string ActingAsType = "x-hrsystem-acting-as-type";

    /// <summary>SHA-256-truncated client IP (forwarded by the gateway, never the raw IP).</summary>
    public const string ClientIpHash = "x-hrsystem-client-ip-hash";

    /// <summary>Client User-Agent forwarded by the gateway (truncated).</summary>
    public const string UserAgent = "x-hrsystem-user-agent";
}
