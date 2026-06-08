using EmployeeService.Repositories;
using EmployeeService.Services;
using HRSystem.Shared;
using HRSystem.Shared.Grpc;

var builder = WebApplication.CreateBuilder(args);

// Aspire ServiceDefaults (service discovery, resilience, health checks, OTel)
builder.AddServiceDefaults();

// Shared infrastructure (Cosmos, Redis, Telemetry, Interceptors)
builder.Services.AddHRSystemShared(builder.Configuration, "employee-service");

// Repositories — use InMemory when Cosmos is not configured
var cosmosConn = builder.Configuration["Cosmos:ConnectionString"]
    ?? builder.Configuration.GetConnectionString("cosmosdb");
if (string.IsNullOrEmpty(cosmosConn))
{
    builder.Services.AddSingleton<IEmployeeRepository, InMemoryEmployeeRepository>();
}
else
{
    builder.Services.AddScoped<IEmployeeRepository, CosmosEmployeeRepository>();
}

// gRPC
builder.Services.AddGrpc(options =>
{
    options.Interceptors.Add<LoggingInterceptor>();
    options.Interceptors.Add<ValidationInterceptor>();
});
builder.Services.AddGrpcHealthChecks();
builder.Services.AddGrpcReflection();

// Kestrel — only bind manually when NOT running under Aspire
if (string.IsNullOrEmpty(builder.Configuration["ASPNETCORE_URLS"]))
{
    var grpcPort = builder.Configuration["GRPC_PORT"] ?? "50051";
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

var app = builder.Build();

// gRPC endpoints
app.MapDefaultEndpoints();
app.MapGrpcService<EmployeeGrpcService>();
app.MapGrpcHealthChecksService();

if (app.Environment.IsDevelopment())
{
    app.MapGrpcReflectionService();
}

app.Run();
