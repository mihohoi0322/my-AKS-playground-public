using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Logging;

namespace HRSystem.Shared.Grpc;

public class ValidationInterceptor : Interceptor
{
    private readonly ILogger<ValidationInterceptor> _logger;

    public ValidationInterceptor(ILogger<ValidationInterceptor> logger)
    {
        _logger = logger;
    }

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        if (request is null)
        {
            _logger.LogWarning("Null request received for {Method}", context.Method);
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Request cannot be null"));
        }

        return await continuation(request, context);
    }
}
