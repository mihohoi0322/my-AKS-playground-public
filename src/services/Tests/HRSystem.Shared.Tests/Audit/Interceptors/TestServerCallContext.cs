using Grpc.Core;

namespace HRSystem.Shared.Tests.Audit.Interceptors;

/// <summary>
/// Minimal <see cref="ServerCallContext"/> fake. <c>Grpc.Core.Testing</c> is not used because
/// it pulls the legacy native runtime; this stub satisfies the abstract surface the
/// interceptor reads (Method + RequestHeaders + CancellationToken).
/// </summary>
internal sealed class TestServerCallContext : ServerCallContext
{
    private readonly string _method;
    private readonly Metadata _requestHeaders;
    private readonly Metadata _responseTrailers = new();
    private Status _status = Status.DefaultSuccess;
    private WriteOptions? _writeOptions;

    public TestServerCallContext(string method, Metadata? requestHeaders = null)
    {
        _method = method;
        _requestHeaders = requestHeaders ?? new Metadata();
    }

    protected override string MethodCore => _method;
    protected override string HostCore => "localhost";
    protected override string PeerCore => "ipv4:127.0.0.1:1";
    protected override DateTime DeadlineCore => DateTime.MaxValue;
    protected override Metadata RequestHeadersCore => _requestHeaders;
    protected override CancellationToken CancellationTokenCore => CancellationToken.None;
    protected override Metadata ResponseTrailersCore => _responseTrailers;
    protected override Status StatusCore { get => _status; set => _status = value; }
    protected override WriteOptions? WriteOptionsCore { get => _writeOptions; set => _writeOptions = value; }
    protected override AuthContext AuthContextCore => new("", new Dictionary<string, List<AuthProperty>>());

    protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options)
    {
        throw new NotSupportedException();
    }

    protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
}
