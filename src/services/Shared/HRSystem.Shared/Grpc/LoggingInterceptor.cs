using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Logging;

namespace HRSystem.Shared.Grpc;

public class LoggingInterceptor : Interceptor
{
    private readonly ILogger<LoggingInterceptor> _logger;

    public LoggingInterceptor(ILogger<LoggingInterceptor> logger)
    {
        _logger = logger;
    }

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        var method = context.Method;
        _logger.LogInformation("gRPC call started: {Method}", method);

        try
        {
            var response = await continuation(request, context);
            _logger.LogInformation("gRPC call completed: {Method}", method);
            return response;
        }
        catch (RpcException ex)
        {
            _logger.LogWarning(ex, "gRPC call failed: {Method}, Status: {StatusCode}", method, ex.StatusCode);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "gRPC call error: {Method}", method);
            throw new RpcException(new Status(StatusCode.Internal, "Internal server error"));
        }
    }
}
