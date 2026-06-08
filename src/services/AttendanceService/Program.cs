using AttendanceService.Repositories;
using AttendanceService.Services;
using HRSystem.Shared;
using HRSystem.Shared.Grpc;
using HRSystem.Protos.Employee.V1;

var builder = WebApplication.CreateBuilder(args);

// Aspire ServiceDefaults (service discovery, resilience, health checks, OTel)
builder.AddServiceDefaults();

// Kestrel — only bind manually when NOT running under Aspire
if (string.IsNullOrEmpty(builder.Configuration["ASPNETCORE_URLS"]))
{
    var grpcPort = builder.Configuration["GRPC_PORT"] ?? "50052";
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.ListenAnyIP(int.Parse(grpcPort), listenOptions =>
        {
            listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2;
        });
    });
}
else
{
    // Under Aspire: enforce HTTP/2 on all endpoints for gRPC
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.ConfigureEndpointDefaults(listenOptions =>
        {
            listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2;
        });
    });
}

// Shared services (Cosmos, Redis, Telemetry, Interceptors)
builder.Services.AddHRSystemShared(builder.Configuration, "attendance-service");

// Repository — use InMemory when Cosmos is not configured
var cosmosConn = builder.Configuration["Cosmos:ConnectionString"]
    ?? builder.Configuration.GetConnectionString("cosmosdb");
if (string.IsNullOrEmpty(cosmosConn))
{
    builder.Services.AddSingleton<IAttendanceRepository, InMemoryAttendanceRepository>();
}
else
{
    builder.Services.AddSingleton<IAttendanceRepository, CosmosAttendanceRepository>();
}

// gRPC server with interceptors
builder.Services.AddGrpc(options =>
{
    options.Interceptors.Add<LoggingInterceptor>();
    options.Interceptors.Add<ValidationInterceptor>();
});

// gRPC client for EmployeeService
var employeeServiceUrl = builder.Configuration["EMPLOYEE_SERVICE_URL"] ?? "http://employee-service:50051";
builder.Services.AddGrpcClient<EmployeeService.EmployeeServiceClient>(o =>
{
    o.Address = new Uri(employeeServiceUrl);
});

// Health checks
builder.Services.AddGrpcHealthChecks();

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapGrpcService<AttendanceGrpcService>();
app.MapGrpcHealthChecksService();
app.MapGet("/", () => "AttendanceService gRPC is running");

app.Run();
