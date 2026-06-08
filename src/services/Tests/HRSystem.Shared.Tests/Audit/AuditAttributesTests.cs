using System.Reflection;
using HRSystem.Shared.Audit;

namespace HRSystem.Shared.Tests.Audit;

public sealed class AuditAttributesTests
{
    private sealed class Target
    {
        [Pii(Strategy = "hash")]
        public string Email { get; set; } = string.Empty;

        [Sensitive]
        public string ApiKey { get; set; } = string.Empty;

        public string Public { get; set; } = string.Empty;
    }

    [Fact]
    public void PiiAttribute_IsDiscoverableViaReflection_AndCarriesStrategy()
    {
        var prop = typeof(Target).GetProperty(nameof(Target.Email))!;
        var attr = prop.GetCustomAttribute<PiiAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("hash", attr!.Strategy);
    }

    [Fact]
    public void SensitiveAttribute_IsDiscoverableViaReflection()
    {
        var prop = typeof(Target).GetProperty(nameof(Target.ApiKey))!;
        Assert.NotNull(prop.GetCustomAttribute<SensitiveAttribute>());
    }

    [Fact]
    public void UnannotatedProperty_HasNeitherAttribute()
    {
        var prop = typeof(Target).GetProperty(nameof(Target.Public))!;
        Assert.Null(prop.GetCustomAttribute<PiiAttribute>());
        Assert.Null(prop.GetCustomAttribute<SensitiveAttribute>());
    }

    [Fact]
    public void PiiAttribute_AppliesToFields()
    {
        // Phase 1 contract: AttributeUsage allows fields too (records / structs use them).
        var usage = typeof(PiiAttribute).GetCustomAttribute<AttributeUsageAttribute>()!;
        Assert.True(usage.ValidOn.HasFlag(AttributeTargets.Property));
        Assert.True(usage.ValidOn.HasFlag(AttributeTargets.Field));
        Assert.False(usage.AllowMultiple);
    }
}
