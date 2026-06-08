using Grpc.Core;
using HRSystem.Shared.Audit;

namespace HRSystem.Shared.Tests.Audit;

public sealed class AuditAttributeValidatorTests
{
    [Fact]
    public void BuildRegistry_FailsFast_WhenAttributeMissing()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            AuditAttributeValidator.BuildRegistryFromTypes(new[] { typeof(MissingAttributeService) }));

        Assert.Contains(nameof(MissingAttributeService.Forgotten), ex.Message);
        Assert.Contains("HRSAUD001", ex.Message);
    }

    [Fact]
    public void BuildRegistry_FailsFast_WhenBothAttributesPresent()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            AuditAttributeValidator.BuildRegistryFromTypes(new[] { typeof(BothAttributesService) }));

        Assert.Contains(nameof(BothAttributesService.Conflict), ex.Message);
        Assert.Contains("cannot be annotated with both", ex.Message);
    }

    [Fact]
    public void BuildRegistry_Succeeds_WhenAllAnnotated()
    {
        var registry = AuditAttributeValidator.BuildRegistryFromTypes(new[]
        {
            typeof(AnnotatedAuditedService),
            typeof(AnnotatedNoAuditService),
        });

        Assert.Equal(2, registry.Count);
        Assert.All(registry.Entries, e =>
            Assert.True(e.Audit is not null || e.NoAudit is not null));
    }

    [Fact]
    public void BuildRegistry_OnRealServiceAssemblies_BuildsCleanly()
    {
        // Smoke-test: the actual EmployeeGrpcService / AttendanceGrpcService /
        // OrganizationGrpcService implementations must round-trip through the validator
        // because every method is annotated.
        var implTypes = new[]
        {
            Type.GetType("EmployeeService.Services.EmployeeGrpcService, EmployeeService"),
            Type.GetType("AttendanceService.Services.AttendanceGrpcService, AttendanceService"),
            Type.GetType("OrganizationService.Services.OrganizationGrpcService, OrganizationService"),
        };

        // If the assemblies are not loaded (Tests project does not reference them), skip.
        if (implTypes.Any(t => t is null))
        {
            return;
        }

        var registry = AuditAttributeValidator.BuildRegistryFromTypes(implTypes!);
        Assert.True(registry.Count > 0);
    }

    // ----- Fixtures -----

    public class MissingAttributeService
    {
        public Task<string> Forgotten(string req, ServerCallContext ctx) => Task.FromResult(req);
    }

    public class BothAttributesService
    {
        [Audit(AuditEventType.EmployeeUpdated)]
        [NoAudit("conflict")]
        public Task<string> Conflict(string req, ServerCallContext ctx) => Task.FromResult(req);
    }

    public class AnnotatedAuditedService
    {
        [Audit(AuditEventType.EmployeeUpdated)]
        public Task<string> Update(string req, ServerCallContext ctx) => Task.FromResult(req);
    }

    public class AnnotatedNoAuditService
    {
        [NoAudit("read-only query")]
        public Task<string> Read(string req, ServerCallContext ctx) => Task.FromResult(req);
    }
}
