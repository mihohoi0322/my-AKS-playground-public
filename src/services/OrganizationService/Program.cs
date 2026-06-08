using HRSystem.Shared;
using HRSystem.Shared.Grpc;
using OrganizationService.Repositories;
using OrganizationService.Services;

var builder = WebApplication.CreateBuilder(args);

// Aspire ServiceDefaults (service discovery, resilience, health checks, OTel)
builder.AddServiceDefaults();

// Kestrel — only bind manually when NOT running under Aspire
if (string.IsNullOrEmpty(builder.Configuration["ASPNETCORE_URLS"]))
{
    var grpcPort = builder.Configuration["GRPC_PORT"] ?? "50053";
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

// Shared infrastructure: Cosmos, Redis, OTel, gRPC interceptors
builder.Services.AddHRSystemShared(builder.Configuration, "organization-service");

// Repository — use InMemory when Cosmos is not configured
var cosmosConn = builder.Configuration["Cosmos:ConnectionString"]
    ?? builder.Configuration.GetConnectionString("cosmosdb");
if (string.IsNullOrEmpty(cosmosConn))
{
    builder.Services.AddSingleton<IOrganizationRepository, InMemoryOrganizationRepository>();
}
else
{
    builder.Services.AddSingleton<IOrganizationRepository, CosmosOrganizationRepository>();
}

// gRPC
builder.Services.AddGrpc(options =>
{
    options.Interceptors.Add<LoggingInterceptor>();
    options.Interceptors.Add<ValidationInterceptor>();
});
builder.Services.AddGrpcHealthChecks();
builder.Services.AddGrpcReflection();

// gRPC client for EmployeeService — used by DeleteOrganization to gate physical deletion
// on department membership (HasEmployeesByDepartment). Same pattern as AttendanceService.
var employeeServiceUrl = builder.Configuration["EMPLOYEE_SERVICE_URL"] ?? "http://employee-service:50051";
builder.Services.AddGrpcClient<HRSystem.Protos.Employee.V1.EmployeeService.EmployeeServiceClient>(o =>
{
    o.Address = new Uri(employeeServiceUrl);
});

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapGrpcService<OrganizationGrpcService>();
app.MapGrpcHealthChecksService();

if (app.Environment.IsDevelopment())
{
    app.MapGrpcReflectionService();
}

app.Run();
