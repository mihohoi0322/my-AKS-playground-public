var builder = DistributedApplication.CreateBuilder(args);

// ========================================
// Infrastructure
// ========================================
var redis = builder.AddRedis("redis");

// Cosmos DB Emulator — requires large Docker image pull (~2GB).
// Set ENABLE_COSMOS_EMULATOR=true to enable; otherwise services degrade gracefully.
var enableCosmos = Environment.GetEnvironmentVariable("ENABLE_COSMOS_EMULATOR") == "true";
IResourceBuilder<AzureCosmosDBResource>? cosmos = null;
if (enableCosmos)
{
    cosmos = builder.AddAzureCosmosDB("cosmosdb")
        .RunAsEmulator();
}

// ========================================
// C# gRPC backend services
// ========================================
var employeeService = builder.AddProject<Projects.EmployeeService>("employee-service")
    .WithReference(redis)
    .WaitFor(redis)
    .WithEnvironment("COSMOS_DATABASE", "hrsystem");

var attendanceService = builder.AddProject<Projects.AttendanceService>("attendance-service")
    .WithReference(redis)
    .WaitFor(redis)
    .WithReference(employeeService)
    .WithEnvironment("COSMOS_DATABASE", "hrsystem");

var organizationService = builder.AddProject<Projects.OrganizationService>("organization-service")
    .WithReference(redis)
    .WaitFor(redis)
    .WithReference(employeeService)
    .WithEnvironment("COSMOS_DATABASE", "hrsystem")
    .WithEnvironment("EMPLOYEE_SERVICE_URL", employeeService.GetEndpoint("http"));

if (cosmos is not null)
{
    employeeService.WithReference(cosmos).WaitFor(cosmos);
    attendanceService.WithReference(cosmos).WaitFor(cosmos);
    organizationService.WithReference(cosmos).WaitFor(cosmos);
}

// ========================================
// Node.js apps (via Aspire.Hosting.JavaScript)
// ========================================

// api-gateway expects gRPC endpoints as "host:port" (no scheme).
// Aspire's WithReference sets env vars like "services__employee-service__http__0"
// which is "http://host:port" — unusable by @grpc/grpc-js directly.
// We explicitly set the env vars the api-gateway expects.
var apiGateway = builder.AddJavaScriptApp("api-gateway", "../../../packages/api-gateway")
    .WithHttpEndpoint(port: 8000, env: "APP_PORT")
    .WithReference(redis)
    .WithEnvironment("EMPLOYEE_SERVICE_URL", employeeService.GetEndpoint("http"))
    .WithEnvironment("ATTENDANCE_SERVICE_URL", attendanceService.GetEndpoint("http"))
    .WithEnvironment("ORGANIZATION_SERVICE_URL", organizationService.GetEndpoint("http"))
    .WithEnvironment("REDIS_ENABLED", "true")
    .WithEnvironment("TELEMETRY_ENABLED", "false");

var webUi = builder.AddJavaScriptApp("web-ui", "../../../packages/web-ui")
    .WithHttpEndpoint(port: 3001, env: "PORT")
    .WithReference(apiGateway)
    .WithEnvironment("NEXT_PUBLIC_API_URL", "http://localhost:8000");

builder.Build().Run();
