using Grpc.Core;
using HRSystem.Protos.Employee.V1;
using HRSystem.Protos.Organization.V1;
using Microsoft.Extensions.Logging;
using Moq;
using OrganizationService.Models;
using OrganizationService.Repositories;
using OrganizationService.Services;

namespace OrganizationService.Tests;

internal sealed class TestServerCallContext : ServerCallContext
{
    protected override string MethodCore => "TestMethod";
    protected override string HostCore => "localhost";
    protected override string PeerCore => "test-peer";
    protected override DateTime DeadlineCore => DateTime.UtcNow.AddMinutes(1);
    protected override Metadata RequestHeadersCore => new();
    protected override CancellationToken CancellationTokenCore => CancellationToken.None;
    protected override Metadata ResponseTrailersCore => new();
    protected override Status StatusCore { get; set; }
    protected override WriteOptions? WriteOptionsCore { get; set; }
    protected override AuthContext AuthContextCore => new(string.Empty, new Dictionary<string, List<AuthProperty>>());
    protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) => throw new NotImplementedException();
    protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
}

public class OrganizationGrpcServiceTests
{
    private readonly Mock<IOrganizationRepository> _repoMock;
    private readonly Mock<EmployeeService.EmployeeServiceClient> _employeeClientMock;
    private readonly OrganizationGrpcService _sut;
    private readonly ServerCallContext _context;

    public OrganizationGrpcServiceTests()
    {
        _repoMock = new Mock<IOrganizationRepository>();
        _employeeClientMock = new Mock<EmployeeService.EmployeeServiceClient>();
        var loggerMock = new Mock<ILogger<OrganizationGrpcService>>();
        _sut = new OrganizationGrpcService(_repoMock.Object, _employeeClientMock.Object, loggerMock.Object);
        _context = new TestServerCallContext();
    }

    private static AsyncUnaryCall<T> CreateAsyncUnaryCall<T>(T response)
    {
        return new AsyncUnaryCall<T>(
            Task.FromResult(response),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });
    }

    private static AsyncUnaryCall<T> CreateFailedAsyncUnaryCall<T>(StatusCode statusCode, string detail = "")
    {
        var tcs = new TaskCompletionSource<T>();
        tcs.SetException(new RpcException(new Status(statusCode, detail)));
        return new AsyncUnaryCall<T>(
            tcs.Task,
            Task.FromResult(new Metadata()),
            () => new Status(statusCode, detail),
            () => new Metadata(),
            () => { });
    }

    private void SetupHasEmployees(string departmentId, bool hasEmployees)
    {
        _employeeClientMock
            .Setup(c => c.HasEmployeesByDepartmentAsync(
                It.Is<HasEmployeesByDepartmentRequest>(r => r.DepartmentId == departmentId),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncUnaryCall(new HasEmployeesByDepartmentResponse { HasEmployees = hasEmployees }));
    }

    // ── CreateOrganization ──────────────────────────────────────

    [Fact]
    public async Task CreateOrganization_Success_ReturnsOrganization()
    {
        var request = new CreateOrganizationRequest
        {
            Name = "Engineering",
            ParentOrgId = "parent-1",
            ManagerId = "mgr-1",
            Description = "Eng dept"
        };

        var created = new OrganizationDocument
        {
            OrgId = "org-123",
            Name = "Engineering",
            ParentOrgId = "parent-1",
            Level = 1,
            ManagerId = "mgr-1",
            Description = "Eng dept"
        };

        _repoMock
            .Setup(r => r.CreateAsync(It.IsAny<OrganizationDocument>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(created);

        var result = await _sut.CreateOrganization(request, _context);

        Assert.Equal("org-123", result.OrgId);
        Assert.Equal("Engineering", result.Name);
        Assert.Equal("parent-1", result.ParentOrgId);
        Assert.Equal(1, result.Level);
        Assert.Equal("mgr-1", result.ManagerId);
    }

    [Fact]
    public async Task CreateOrganization_EmptyName_ThrowsInvalidArgument()
    {
        var request = new CreateOrganizationRequest { Name = "" };

        var ex = await Assert.ThrowsAsync<RpcException>(
            () => _sut.CreateOrganization(request, _context));

        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    // ── GetOrganization ─────────────────────────────────────────

    [Fact]
    public async Task GetOrganization_Success_ReturnsOrganization()
    {
        var doc = new OrganizationDocument
        {
            OrgId = "org-1",
            Name = "Sales",
            ParentOrgId = "",
            Level = 0,
            ManagerId = "mgr-2",
            Description = "Sales dept"
        };

        _repoMock
            .Setup(r => r.GetAsync("org-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(doc);

        var result = await _sut.GetOrganization(
            new GetOrganizationRequest { OrgId = "org-1" }, _context);

        Assert.Equal("org-1", result.OrgId);
        Assert.Equal("Sales", result.Name);
    }

    [Fact]
    public async Task GetOrganization_NotFound_ThrowsNotFound()
    {
        _repoMock
            .Setup(r => r.GetAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrganizationDocument?)null);

        var ex = await Assert.ThrowsAsync<RpcException>(
            () => _sut.GetOrganization(
                new GetOrganizationRequest { OrgId = "missing" }, _context));

        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
    }

    // ── UpdateOrganization ──────────────────────────────────────

    [Fact]
    public async Task UpdateOrganization_Success_ReturnsUpdatedOrganization()
    {
        var existing = new OrganizationDocument
        {
            OrgId = "org-1",
            Name = "Old Name",
            ParentOrgId = "",
            Level = 0,
            ManagerId = "mgr-1",
            Description = "old"
        };

        _repoMock
            .Setup(r => r.GetAsync("org-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        _repoMock
            .Setup(r => r.UpdateAsync(It.IsAny<OrganizationDocument>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrganizationDocument d, CancellationToken _) => d);

        // Build request with optional name set
        var request = new UpdateOrganizationRequest { OrgId = "org-1", Name = "New Name" };

        var result = await _sut.UpdateOrganization(request, _context);

        Assert.Equal("org-1", result.OrgId);
        Assert.Equal("New Name", result.Name);
    }

    [Fact]
    public async Task UpdateOrganization_NotFound_ThrowsNotFound()
    {
        _repoMock
            .Setup(r => r.GetAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrganizationDocument?)null);

        var request = new UpdateOrganizationRequest { OrgId = "missing" };

        var ex = await Assert.ThrowsAsync<RpcException>(
            () => _sut.UpdateOrganization(request, _context));

        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
    }

    // ── ListChildren ────────────────────────────────────────────

    [Fact]
    public async Task ListChildren_WithResults_ReturnsOrganizationsAndCursor()
    {
        var items = new List<OrganizationDocument>
        {
            new() { OrgId = "child-1", Name = "Child 1", ParentOrgId = "org-1", Level = 1 },
            new() { OrgId = "child-2", Name = "Child 2", ParentOrgId = "org-1", Level = 1 }
        };

        _repoMock
            .Setup(r => r.ListChildrenAsync("org-1", 20, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((items.AsReadOnly(), (string?)"cursor-abc", true));

        var result = await _sut.ListChildren(
            new ListChildrenRequest { OrgId = "org-1" }, _context);

        Assert.Equal(2, result.Organizations.Count);
        Assert.Equal("child-1", result.Organizations[0].OrgId);
        Assert.Equal("child-2", result.Organizations[1].OrgId);
        Assert.Equal("cursor-abc", result.NextCursor);
        Assert.True(result.HasMore);
    }

    [Fact]
    public async Task ListChildren_EmptyResults_ReturnsEmptyList()
    {
        var empty = new List<OrganizationDocument>();

        _repoMock
            .Setup(r => r.ListChildrenAsync("org-1", 20, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((empty.AsReadOnly(), (string?)null, false));

        var result = await _sut.ListChildren(
            new ListChildrenRequest { OrgId = "org-1" }, _context);

        Assert.Empty(result.Organizations);
        Assert.Equal(string.Empty, result.NextCursor);
        Assert.False(result.HasMore);
    }

    // ── GetOrganizationTree ─────────────────────────────────────

    [Fact]
    public async Task GetOrganizationTree_Success_BuildsRecursiveTree()
    {
        // Root → [Child A → [Grandchild], Child B]
        var root = new OrganizationDocument
        {
            OrgId = "root",
            Name = "Root",
            Level = 0,
            ManagerId = "mgr-root"
        };
        var childA = new OrganizationDocument
        {
            OrgId = "child-a",
            Name = "Child A",
            Level = 1,
            ManagerId = "mgr-a"
        };
        var childB = new OrganizationDocument
        {
            OrgId = "child-b",
            Name = "Child B",
            Level = 1,
            ManagerId = "mgr-b"
        };
        var grandchild = new OrganizationDocument
        {
            OrgId = "grandchild",
            Name = "Grandchild",
            Level = 2,
            ManagerId = "mgr-gc"
        };

        _repoMock
            .Setup(r => r.GetAsync("root", It.IsAny<CancellationToken>()))
            .ReturnsAsync(root);

        _repoMock
            .Setup(r => r.GetChildrenAsync("root", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OrganizationDocument> { childA, childB }.AsReadOnly());

        _repoMock
            .Setup(r => r.GetChildrenAsync("child-a", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OrganizationDocument> { grandchild }.AsReadOnly());

        _repoMock
            .Setup(r => r.GetChildrenAsync("child-b", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OrganizationDocument>().AsReadOnly());

        _repoMock
            .Setup(r => r.GetChildrenAsync("grandchild", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OrganizationDocument>().AsReadOnly());

        var result = await _sut.GetOrganizationTree(
            new GetOrganizationTreeRequest { OrgId = "root" }, _context);

        // Root node
        Assert.Equal("root", result.OrgId);
        Assert.Equal("Root", result.Name);
        Assert.Equal(0, result.Level);
        Assert.Equal(2, result.Children.Count);

        // Child A with grandchild
        var nodeA = result.Children.First(c => c.OrgId == "child-a");
        Assert.Equal("Child A", nodeA.Name);
        Assert.Single(nodeA.Children);
        Assert.Equal("grandchild", nodeA.Children[0].OrgId);
        Assert.Empty(nodeA.Children[0].Children);

        // Child B (leaf)
        var nodeB = result.Children.First(c => c.OrgId == "child-b");
        Assert.Equal("Child B", nodeB.Name);
        Assert.Empty(nodeB.Children);
    }

    [Fact]
    public async Task GetOrganizationTree_NotFound_ThrowsNotFound()
    {
        _repoMock
            .Setup(r => r.GetAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrganizationDocument?)null);

        var ex = await Assert.ThrowsAsync<RpcException>(
            () => _sut.GetOrganizationTree(
                new GetOrganizationTreeRequest { OrgId = "missing" }, _context));

        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
    }

    // ── DeleteOrganization ──────────────────────────────────────

    // Use real UUIDs so the new orgId-format validation does not short-circuit these tests.
    private const string OrgIdA = "11111111-1111-4111-8111-111111111111";
    private const string OrgIdMissing = "22222222-2222-4222-8222-222222222222";
    private const string OrgIdChild = "33333333-3333-4333-8333-333333333333";

    private static OrganizationDocument MakeOrg(string orgId, string name = "Org") => new()
    {
        OrgId = orgId,
        Id = orgId,
        Name = name,
        ParentOrgId = "",
        Level = 0,
        ManagerId = "mgr-1",
        Description = ""
    };

    [Fact]
    public async Task DeleteOrganization_EmptyId_ThrowsInvalidArgument()
    {
        var ex = await Assert.ThrowsAsync<RpcException>(
            () => _sut.DeleteOrganization(new DeleteOrganizationRequest { OrgId = "" }, _context));

        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    [Fact]
    public async Task DeleteOrganization_WhitespaceId_ThrowsInvalidArgument()
    {
        var ex = await Assert.ThrowsAsync<RpcException>(
            () => _sut.DeleteOrganization(new DeleteOrganizationRequest { OrgId = "   " }, _context));

        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
        _repoMock.Verify(r => r.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteOrganization_MalformedUuid_ThrowsInvalidArgument()
    {
        var ex = await Assert.ThrowsAsync<RpcException>(
            () => _sut.DeleteOrganization(new DeleteOrganizationRequest { OrgId = "not-a-uuid" }, _context));

        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
        // Validation must short-circuit before any I/O.
        _repoMock.Verify(r => r.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _repoMock.Verify(r => r.HasChildrenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteOrganization_NotFound_ThrowsNotFound()
    {
        _repoMock
            .Setup(r => r.GetAsync(OrgIdMissing, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrganizationDocument?)null);

        var ex = await Assert.ThrowsAsync<RpcException>(
            () => _sut.DeleteOrganization(new DeleteOrganizationRequest { OrgId = OrgIdMissing }, _context));

        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
        // Children/employee checks must NOT have been performed.
        _repoMock.Verify(r => r.HasChildrenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _employeeClientMock.Verify(c => c.HasEmployeesByDepartmentAsync(
            It.IsAny<HasEmployeesByDepartmentRequest>(),
            It.IsAny<Metadata>(),
            It.IsAny<DateTime?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _repoMock.Verify(r => r.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteOrganization_HasChildren_ThrowsFailedPreconditionWithoutEmployeeCall()
    {
        var org = MakeOrg(OrgIdA);

        _repoMock.Setup(r => r.GetAsync(OrgIdA, It.IsAny<CancellationToken>())).ReturnsAsync(org);
        _repoMock.Setup(r => r.HasChildrenAsync(OrgIdA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var ex = await Assert.ThrowsAsync<RpcException>(
            () => _sut.DeleteOrganization(new DeleteOrganizationRequest { OrgId = OrgIdA }, _context));

        Assert.Equal(StatusCode.FailedPrecondition, ex.StatusCode);
        // Children check precedes (and short-circuits) the cross-service employee check.
        _employeeClientMock.Verify(c => c.HasEmployeesByDepartmentAsync(
            It.IsAny<HasEmployeesByDepartmentRequest>(),
            It.IsAny<Metadata>(),
            It.IsAny<DateTime?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _repoMock.Verify(r => r.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteOrganization_HasEmployees_ThrowsFailedPrecondition()
    {
        var org = MakeOrg(OrgIdA);

        _repoMock.Setup(r => r.GetAsync(OrgIdA, It.IsAny<CancellationToken>())).ReturnsAsync(org);
        _repoMock.Setup(r => r.HasChildrenAsync(OrgIdA, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        SetupHasEmployees(OrgIdA, hasEmployees: true);

        var ex = await Assert.ThrowsAsync<RpcException>(
            () => _sut.DeleteOrganization(new DeleteOrganizationRequest { OrgId = OrgIdA }, _context));

        Assert.Equal(StatusCode.FailedPrecondition, ex.StatusCode);
        _repoMock.Verify(r => r.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteOrganization_EmployeeServiceUnavailable_ThrowsUnavailable()
    {
        var org = MakeOrg(OrgIdA);

        _repoMock.Setup(r => r.GetAsync(OrgIdA, It.IsAny<CancellationToken>())).ReturnsAsync(org);
        _repoMock.Setup(r => r.HasChildrenAsync(OrgIdA, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        _employeeClientMock
            .Setup(c => c.HasEmployeesByDepartmentAsync(
                It.IsAny<HasEmployeesByDepartmentRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateFailedAsyncUnaryCall<HasEmployeesByDepartmentResponse>(StatusCode.Unavailable, "down"));

        var ex = await Assert.ThrowsAsync<RpcException>(
            () => _sut.DeleteOrganization(new DeleteOrganizationRequest { OrgId = OrgIdA }, _context));

        Assert.Equal(StatusCode.Unavailable, ex.StatusCode);
        _repoMock.Verify(r => r.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteOrganization_AllChecksPass_ReturnsEmptyAndDeletes()
    {
        var org = MakeOrg(OrgIdA, "Engineering");

        _repoMock.Setup(r => r.GetAsync(OrgIdA, It.IsAny<CancellationToken>())).ReturnsAsync(org);
        _repoMock.Setup(r => r.HasChildrenAsync(OrgIdA, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        SetupHasEmployees(OrgIdA, hasEmployees: false);
        _repoMock.Setup(r => r.DeleteAsync(OrgIdA, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await _sut.DeleteOrganization(
            new DeleteOrganizationRequest { OrgId = OrgIdA }, _context);

        Assert.NotNull(result);
        _repoMock.Verify(r => r.DeleteAsync(OrgIdA, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteOrganization_DeleteAsyncReturnsFalse_ThrowsNotFound()
    {
        // Race: org disappears between Get and Delete (idempotency note in IOrganizationRepository).
        var org = MakeOrg(OrgIdA);

        _repoMock.Setup(r => r.GetAsync(OrgIdA, It.IsAny<CancellationToken>())).ReturnsAsync(org);
        _repoMock.Setup(r => r.HasChildrenAsync(OrgIdA, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        SetupHasEmployees(OrgIdA, hasEmployees: false);
        _repoMock.Setup(r => r.DeleteAsync(OrgIdA, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var ex = await Assert.ThrowsAsync<RpcException>(
            () => _sut.DeleteOrganization(new DeleteOrganizationRequest { OrgId = OrgIdA }, _context));

        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
    }
}

public class InMemoryOrganizationRepositoryHasChildrenTests
{
    [Fact]
    public async Task HasChildrenAsync_NoChildren_ReturnsFalse()
    {
        var repo = new InMemoryOrganizationRepository();
        await repo.CreateAsync(new OrganizationDocument { OrgId = "parent", Name = "Parent", ParentOrgId = "" });

        var has = await repo.HasChildrenAsync("parent");

        Assert.False(has);
    }

    [Fact]
    public async Task HasChildrenAsync_SingleChild_ReturnsTrue()
    {
        var repo = new InMemoryOrganizationRepository();
        await repo.CreateAsync(new OrganizationDocument { OrgId = "parent", Name = "Parent", ParentOrgId = "" });
        await repo.CreateAsync(new OrganizationDocument { OrgId = "child-1", Name = "Child 1", ParentOrgId = "parent" });

        var has = await repo.HasChildrenAsync("parent");

        Assert.True(has);
    }

    [Fact]
    public async Task HasChildrenAsync_MultipleChildren_ReturnsTrue()
    {
        var repo = new InMemoryOrganizationRepository();
        await repo.CreateAsync(new OrganizationDocument { OrgId = "parent", Name = "Parent", ParentOrgId = "" });
        await repo.CreateAsync(new OrganizationDocument { OrgId = "c1", Name = "C1", ParentOrgId = "parent" });
        await repo.CreateAsync(new OrganizationDocument { OrgId = "c2", Name = "C2", ParentOrgId = "parent" });

        var has = await repo.HasChildrenAsync("parent");

        Assert.True(has);
    }

    [Fact]
    public async Task HasChildrenAsync_EmptyParentId_ReturnsFalse()
    {
        var repo = new InMemoryOrganizationRepository();
        await repo.CreateAsync(new OrganizationDocument { OrgId = "a", Name = "A", ParentOrgId = "" });

        var has = await repo.HasChildrenAsync("");

        Assert.False(has);
    }
}
