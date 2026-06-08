using EmployeeService.Models;
using EmployeeService.Repositories;
using EmployeeService.Services;
using Grpc.Core;
using HRSystem.Protos.Employee.V1;
using Microsoft.Extensions.Logging;
using Moq;

namespace EmployeeService.Tests;

public class EmployeeGrpcServiceTests
{
    private readonly Mock<IEmployeeRepository> _repoMock = new();
    private readonly Mock<ILogger<EmployeeGrpcService>> _loggerMock = new();
    private readonly EmployeeGrpcService _sut;
    private readonly ServerCallContext _ctx;

    public EmployeeGrpcServiceTests()
    {
        _sut = new EmployeeGrpcService(_repoMock.Object, _loggerMock.Object);
        _ctx = new FakeServerCallContext();
    }

    // ──────────────── CreateEmployee ────────────────

    [Fact]
    public async Task CreateEmployee_Success_ReturnsEmployee()
    {
        var request = new CreateEmployeeRequest
        {
            Name = "Alice",
            Email = "alice@example.com",
            DepartmentId = "dept-1",
            Position = "Engineer",
            HireDate = "2024-01-15"
        };

        _repoMock.Setup(r => r.CreateAsync(It.IsAny<EmployeeDocument>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmployeeDocument doc, CancellationToken _) =>
            {
                doc.EmployeeId = "emp-123";
                doc.Id = "emp-123";
                return doc;
            });

        var result = await _sut.CreateEmployee(request, _ctx);

        Assert.Equal("emp-123", result.EmployeeId);
        Assert.Equal("Alice", result.Name);
        Assert.Equal("alice@example.com", result.Email);
        Assert.Equal("dept-1", result.DepartmentId);
        Assert.Equal("Engineer", result.Position);
        Assert.Equal("2024-01-15", result.HireDate);
        Assert.Equal("active", result.Status);

        _repoMock.Verify(r => r.CreateAsync(
            It.Is<EmployeeDocument>(d =>
                d.Name == "Alice" &&
                d.Email == "alice@example.com" &&
                d.DepartmentId == "dept-1" &&
                d.Status == "active"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateEmployee_EmptyName_ThrowsInvalidArgument()
    {
        var request = new CreateEmployeeRequest { Name = "", Email = "a@b.com" };

        var ex = await Assert.ThrowsAsync<RpcException>(() => _sut.CreateEmployee(request, _ctx));
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
        Assert.Contains("Name", ex.Status.Detail);
    }

    [Fact]
    public async Task CreateEmployee_EmptyEmail_ThrowsInvalidArgument()
    {
        var request = new CreateEmployeeRequest { Name = "Alice", Email = "" };

        var ex = await Assert.ThrowsAsync<RpcException>(() => _sut.CreateEmployee(request, _ctx));
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
        Assert.Contains("Email", ex.Status.Detail);
    }

    [Fact]
    public async Task CreateEmployee_DuplicateEmail_ThrowsAlreadyExists()
    {
        var request = new CreateEmployeeRequest { Name = "Alice", Email = "dup@example.com" };

        _repoMock.Setup(r => r.CreateAsync(It.IsAny<EmployeeDocument>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new EmailAlreadyExistsException("dup@example.com"));

        var ex = await Assert.ThrowsAsync<RpcException>(() => _sut.CreateEmployee(request, _ctx));
        Assert.Equal(StatusCode.AlreadyExists, ex.StatusCode);
    }

    // ──────────────── GetEmployee ────────────────

    [Fact]
    public async Task GetEmployee_Success_ReturnsEmployee()
    {
        var doc = MakeDocument("emp-1");
        _repoMock.Setup(r => r.GetAsync("emp-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(doc);

        var result = await _sut.GetEmployee(new GetEmployeeRequest { EmployeeId = "emp-1" }, _ctx);

        Assert.Equal("emp-1", result.EmployeeId);
        Assert.Equal("Test User", result.Name);
        _repoMock.Verify(r => r.GetAsync("emp-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetEmployee_NotFound_ThrowsNotFound()
    {
        _repoMock.Setup(r => r.GetAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmployeeDocument?)null);

        var ex = await Assert.ThrowsAsync<RpcException>(
            () => _sut.GetEmployee(new GetEmployeeRequest { EmployeeId = "missing" }, _ctx));
        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task GetEmployee_EmptyId_ThrowsInvalidArgument()
    {
        var ex = await Assert.ThrowsAsync<RpcException>(
            () => _sut.GetEmployee(new GetEmployeeRequest { EmployeeId = "" }, _ctx));
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    // ──────────────── UpdateEmployee ────────────────

    [Fact]
    public async Task UpdateEmployee_Success_ReturnsUpdatedEmployee()
    {
        var existing = MakeDocument("emp-1");
        var updated = MakeDocument("emp-1");
        updated.Name = "Updated Name";

        _repoMock.Setup(r => r.GetAsync("emp-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _repoMock.Setup(r => r.UpdateAsync(It.IsAny<EmployeeDocument>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updated);

        var request = new UpdateEmployeeRequest { EmployeeId = "emp-1", Name = "Updated Name" };
        var result = await _sut.UpdateEmployee(request, _ctx);

        Assert.Equal("Updated Name", result.Name);
        _repoMock.Verify(r => r.UpdateAsync(
            It.Is<EmployeeDocument>(d => d.Name == "Updated Name"),
            string.Empty,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateEmployee_NotFound_ThrowsNotFound()
    {
        _repoMock.Setup(r => r.GetAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmployeeDocument?)null);

        var ex = await Assert.ThrowsAsync<RpcException>(
            () => _sut.UpdateEmployee(new UpdateEmployeeRequest { EmployeeId = "missing" }, _ctx));
        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task UpdateEmployee_EmptyId_ThrowsInvalidArgument()
    {
        var ex = await Assert.ThrowsAsync<RpcException>(
            () => _sut.UpdateEmployee(new UpdateEmployeeRequest { EmployeeId = "" }, _ctx));
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    // ──────────────── DeleteEmployee ────────────────

    [Fact]
    public async Task DeleteEmployee_Success_ReturnsDeletedEmployee()
    {
        var doc = MakeDocument("emp-1");
        _repoMock.Setup(r => r.DeleteAsync("emp-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(doc);

        var result = await _sut.DeleteEmployee(new DeleteEmployeeRequest { EmployeeId = "emp-1" }, _ctx);

        Assert.Equal("emp-1", result.EmployeeId);
        _repoMock.Verify(r => r.DeleteAsync("emp-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteEmployee_NotFound_ThrowsNotFound()
    {
        _repoMock.Setup(r => r.DeleteAsync("missing", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Microsoft.Azure.Cosmos.CosmosException(
                "Not found", System.Net.HttpStatusCode.NotFound, 0, "", 0));

        var ex = await Assert.ThrowsAsync<RpcException>(
            () => _sut.DeleteEmployee(new DeleteEmployeeRequest { EmployeeId = "missing" }, _ctx));
        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task DeleteEmployee_EmptyId_ThrowsInvalidArgument()
    {
        var ex = await Assert.ThrowsAsync<RpcException>(
            () => _sut.DeleteEmployee(new DeleteEmployeeRequest { EmployeeId = "" }, _ctx));
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    // ──────────────── ListEmployees ────────────────

    [Fact]
    public async Task ListEmployees_WithResults_ReturnsEmployees()
    {
        var docs = new List<EmployeeDocument> { MakeDocument("emp-1"), MakeDocument("emp-2") };

        _repoMock.Setup(r => r.ListAsync(20, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((docs.AsReadOnly() as IReadOnlyList<EmployeeDocument>, "next-token", true));

        var result = await _sut.ListEmployees(new ListEmployeesRequest(), _ctx);

        Assert.Equal(2, result.Employees.Count);
        Assert.Equal("next-token", result.NextCursor);
        Assert.True(result.HasMore);
    }

    [Fact]
    public async Task ListEmployees_Empty_ReturnsEmptyList()
    {
        var empty = new List<EmployeeDocument>();

        _repoMock.Setup(r => r.ListAsync(20, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((empty.AsReadOnly() as IReadOnlyList<EmployeeDocument>, (string?)null, false));

        var result = await _sut.ListEmployees(new ListEmployeesRequest(), _ctx);

        Assert.Empty(result.Employees);
        Assert.Equal(string.Empty, result.NextCursor);
        Assert.False(result.HasMore);
    }

    [Fact]
    public async Task ListEmployees_WithFilters_PassesParametersCorrectly()
    {
        var docs = new List<EmployeeDocument> { MakeDocument("emp-1") };

        _repoMock.Setup(r => r.ListAsync(10, "cursor-abc", "active", "dept-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((docs.AsReadOnly() as IReadOnlyList<EmployeeDocument>, (string?)null, false));

        var request = new ListEmployeesRequest
        {
            Limit = 10,
            Cursor = "cursor-abc",
            Status = "active",
            DepartmentId = "dept-1"
        };

        var result = await _sut.ListEmployees(request, _ctx);

        Assert.Single(result.Employees);
        _repoMock.Verify(r => r.ListAsync(10, "cursor-abc", "active", "dept-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    // ──────────────── Helpers ────────────────

    private static EmployeeDocument MakeDocument(string id) => new()
    {
        Id = id,
        EmployeeId = id,
        Name = "Test User",
        Email = "test@example.com",
        DepartmentId = "dept-1",
        Position = "Engineer",
        HireDate = "2024-01-15",
        Status = "active"
    };

    /// <summary>
    /// Minimal ServerCallContext implementation for unit tests.
    /// </summary>
    private sealed class FakeServerCallContext : ServerCallContext
    {
        protected override string MethodCore => "test-method";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "test-peer";
        protected override DateTime DeadlineCore => DateTime.MaxValue;
        protected override Metadata RequestHeadersCore => new();
        protected override CancellationToken CancellationTokenCore => CancellationToken.None;
        protected override Metadata ResponseTrailersCore => new();
        protected override Status StatusCore { get; set; }
        protected override WriteOptions? WriteOptionsCore { get; set; }

        protected override AuthContext AuthContextCore =>
            new(string.Empty, new Dictionary<string, List<AuthProperty>>());

        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) =>
            throw new NotImplementedException();

        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) =>
            Task.CompletedTask;
    }
}
