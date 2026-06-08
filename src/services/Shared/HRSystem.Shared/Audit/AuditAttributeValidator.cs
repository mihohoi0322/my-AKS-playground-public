using System.Reflection;
using System.Text;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HRSystem.Shared.Audit;

/// <summary>
/// Startup-time reflection scanner that enforces every gRPC RPC method (a public method that
/// takes <see cref="ServerCallContext"/>) is annotated with either <c>[Audit]</c> or
/// <c>[NoAudit]</c>. Designed to be defence-in-depth for the
/// <see cref="HRSystem.Shared.Audit.AuditAttribute"/> Roslyn analyzer (HRSAUD001) so that
/// missing annotations fail Pod startup rather than slip through reviews.
/// </summary>
public static class AuditAttributeValidator
{
    /// <summary>
    /// Scan the supplied assemblies for gRPC service implementations, validate every RPC method
    /// is annotated with <c>[Audit]</c> or <c>[NoAudit]</c>, and register the resulting
    /// <see cref="AuditMethodRegistry"/> as a singleton. Throws
    /// <see cref="InvalidOperationException"/> on any missing annotation, which cascades to a
    /// Pod startup failure (CrashLoopBackoff) — by design.
    /// </summary>
    public static IHostApplicationBuilder AddAuditAttributeValidation(
        this IHostApplicationBuilder builder,
        params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(assemblies);

        var registry = BuildRegistry(assemblies);
        builder.Services.AddSingleton(registry);
        return builder;
    }

    /// <summary>
    /// Pure validation entry point used directly in tests. Returns a populated registry on
    /// success; throws <see cref="InvalidOperationException"/> listing every offending method
    /// on failure.
    /// </summary>
    public static AuditMethodRegistry BuildRegistry(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        var implTypes = assemblies.SelectMany(EnumerateGrpcServiceImplTypes);
        return BuildRegistryFromTypes(implTypes);
    }

    /// <summary>
    /// Test-friendly overload that validates a specific set of implementation types,
    /// bypassing assembly-wide scanning.
    /// </summary>
    public static AuditMethodRegistry BuildRegistryFromTypes(IEnumerable<Type> implTypes)
    {
        ArgumentNullException.ThrowIfNull(implTypes);

        var entries = new List<AuditMethodMetadata>();
        var errors = new List<string>();

        foreach (var implType in implTypes)
        {
            var grpcServiceName = ResolveGrpcServiceName(implType);
            foreach (var method in EnumerateRpcMethods(implType))
            {
                var audit = method.GetCustomAttribute<AuditAttribute>(inherit: true);
                var noAudit = method.GetCustomAttribute<NoAuditAttribute>(inherit: true);

                if (audit is null && noAudit is null)
                {
                    errors.Add(
                        $"{implType.FullName}.{method.Name} must be annotated with [Audit(eventType)] or [NoAudit(reason)]. " +
                        "See HRSAUD001 / docs/features/audit-log.md.");
                    continue;
                }

                if (audit is not null && noAudit is not null)
                {
                    errors.Add(
                        $"{implType.FullName}.{method.Name} cannot be annotated with both [Audit] and [NoAudit].");
                    continue;
                }

                var grpcPath = grpcServiceName is null
                    ? $"/{implType.Name}/{method.Name}"
                    : $"/{grpcServiceName}/{method.Name}";

                entries.Add(new AuditMethodMetadata(
                    GrpcPath: grpcPath,
                    GrpcServiceName: grpcServiceName ?? implType.Name,
                    MethodName: method.Name,
                    Method: method,
                    Audit: audit,
                    NoAudit: noAudit));
            }
        }

        if (errors.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Audit attribute validation failed:");
            foreach (var e in errors)
            {
                sb.Append(" - ").AppendLine(e);
            }
            throw new InvalidOperationException(sb.ToString());
        }

        // Detect duplicate paths (would mask audit emission).
        var dup = entries.GroupBy(e => e.GrpcPath).FirstOrDefault(g => g.Count() > 1);
        if (dup is not null)
        {
            throw new InvalidOperationException(
                $"Duplicate gRPC method path '{dup.Key}' resolved during audit validation. " +
                "Check that two different service implementations are not sharing the same proto package/service.");
        }

        return new AuditMethodRegistry(entries);
    }

    private static IEnumerable<Type> EnumerateGrpcServiceImplTypes(Assembly assembly)
    {
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.Where(t => t is not null).Cast<Type>().ToArray();
        }

        foreach (var t in types)
        {
            if (t.IsAbstract || t.IsInterface || !t.IsClass)
            {
                continue;
            }
            if (HasAnyRpcMethod(t))
            {
                yield return t;
            }
        }
    }

    private static bool HasAnyRpcMethod(Type type)
    {
        return EnumerateRpcMethods(type).Any();
    }

    /// <summary>
    /// A gRPC RPC entry point: <c>public override</c>, declared on this type (excludes
    /// inherited <c>ServiceBase</c> stubs), and accepts a <see cref="ServerCallContext"/>
    /// parameter (covers unary + every streaming variant).
    /// </summary>
    internal static IEnumerable<MethodInfo> EnumerateRpcMethods(Type type)
    {
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        foreach (var m in methods)
        {
            if (m.IsSpecialName)
            {
                continue;
            }
            if (!m.GetParameters().Any(p => p.ParameterType == typeof(ServerCallContext)))
            {
                continue;
            }
            yield return m;
        }
    }

    /// <summary>
    /// Resolve the gRPC service name (e.g. <c>hrsystem.employee.v1.EmployeeService</c>) from
    /// the implementation type by walking up to <c>XxxServiceBase</c> and reading the outer
    /// generated class's <c>__ServiceName</c> constant.
    /// </summary>
    internal static string? ResolveGrpcServiceName(Type implType)
    {
        var baseType = implType.BaseType;
        while (baseType is not null && baseType != typeof(object))
        {
            var declaring = baseType.DeclaringType;
            if (declaring is not null)
            {
                var field = declaring.GetField(
                    "__ServiceName",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (field is not null && field.IsLiteral && field.GetRawConstantValue() is string name)
                {
                    return name;
                }
                if (field is not null && !field.IsLiteral && field.GetValue(null) is string name2)
                {
                    return name2;
                }
            }
            baseType = baseType.BaseType;
        }
        return null;
    }
}
