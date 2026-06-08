using AttendanceService.Models;
using AttendanceService.Repositories;
using AttendanceService.Services;
using Grpc.Core;
using HRSystem.Protos.Attendance.V1;
using HRSystem.Protos.Employee.V1;
using Microsoft.Extensions.Logging;
using Moq;

namespace AttendanceService.Tests;

public class AttendanceGrpcServiceTests
{
    private readonly Mock<IAttendanceRepository> _repoMock = new();
    private readonly Mock<EmployeeService.EmployeeServiceClient> _employeeClientMock = new();
    private readonly Mock<ILogger<AttendanceGrpcService>> _loggerMock = new();
    private readonly AttendanceGrpcService _sut;
    private readonly ServerCallContext _callContext;

    public AttendanceGrpcServiceTests()
    {
        _sut = new AttendanceGrpcService(_repoMock.Object, _employeeClientMock.Object, _loggerMock.Object);
        _callContext = CreateTestContext();
    }

    #region Helpers

    private static ServerCallContext CreateTestContext()
    {
        return new TestServerCallContext();
    }

    private sealed class TestServerCallContext : ServerCallContext
    {
        protected override string MethodCore => "TestMethod";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "ipv4:127.0.0.1:0";
        protected override DateTime DeadlineCore => DateTime.MaxValue;
        protected override Metadata RequestHeadersCore => new();
        protected override CancellationToken CancellationTokenCore => CancellationToken.None;
        protected override Metadata ResponseTrailersCore => new();
        protected override Status StatusCore { get; set; } = Status.DefaultSuccess;
        protected override WriteOptions? WriteOptionsCore { get; set; }
        protected override AuthContext AuthContextCore => new(string.Empty, new Dictionary<string, List<AuthProperty>>());

        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) => throw new NotImplementedException();
        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
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

    private void SetupEmployeeExists(string employeeId, string status = "active")
    {
        _employeeClientMock
            .Setup(c => c.GetEmployeeAsync(
                It.Is<GetEmployeeRequest>(r => r.EmployeeId == employeeId),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncUnaryCall(new Employee
            {
                EmployeeId = employeeId,
                Name = "Test User",
                Status = status
            }));
    }

    private static AttendanceDocument CreateDocument(
        string employeeId = "emp-1",
        string clockIn = "2024-01-15T09:00:00.0000000Z",
        string clockOut = "",
        double workHours = 0,
        string type = "regular")
    {
        var id = Guid.NewGuid().ToString();
        return new AttendanceDocument
        {
            Id = id,
            AttendanceId = id,
            EmployeeId = employeeId,
            Date = "2024-01-15",
            ClockIn = clockIn,
            ClockOut = clockOut,
            WorkHours = workHours,
            Type = type
        };
    }

    #endregion

    #region ClockIn

    [Fact]
    public async Task ClockIn_Success_ReturnsAttendance()
    {
        const string employeeId = "emp-1";
        SetupEmployeeExists(employeeId);

        _repoMock.Setup(r => r.ClockInAsync(It.IsAny<AttendanceDocument>()))
            .ReturnsAsync((AttendanceDocument doc) => doc);

        var request = new ClockInRequest { EmployeeId = employeeId, Type = "regular" };

        var result = await _sut.ClockIn(request, _callContext);

        Assert.Equal(employeeId, result.EmployeeId);
        Assert.Equal("regular", result.Type);
        Assert.NotEmpty(result.AttendanceId);
        Assert.NotEmpty(result.ClockIn);
        Assert.Empty(result.ClockOut);
        _repoMock.Verify(r => r.ClockInAsync(It.Is<AttendanceDocument>(d => d.EmployeeId == employeeId)), Times.Once);
    }

    [Fact]
    public async Task ClockIn_DefaultType_WhenTypeNotProvided()
    {
        const string employeeId = "emp-1";
        SetupEmployeeExists(employeeId);

        _repoMock.Setup(r => r.ClockInAsync(It.IsAny<AttendanceDocument>()))
            .ReturnsAsync((AttendanceDocument doc) => doc);

        var request = new ClockInRequest { EmployeeId = employeeId };

        var result = await _sut.ClockIn(request, _callContext);

        Assert.Equal("regular", result.Type);
    }

    [Fact]
    public async Task ClockIn_AlreadyClockedIn_ThrowsAlreadyExists()
    {
        const string employeeId = "emp-1";
        SetupEmployeeExists(employeeId);

        _repoMock.Setup(r => r.ClockInAsync(It.IsAny<AttendanceDocument>()))
            .ThrowsAsync(new InvalidOperationException("Already clocked in"));

        var request = new ClockInRequest { EmployeeId = employeeId };

        var ex = await Assert.ThrowsAsync<RpcException>(() => _sut.ClockIn(request, _callContext));
        Assert.Equal(StatusCode.AlreadyExists, ex.StatusCode);
    }

    [Fact]
    public async Task ClockIn_EmptyEmployeeId_ThrowsInvalidArgument()
    {
        var request = new ClockInRequest { EmployeeId = "" };

        var ex = await Assert.ThrowsAsync<RpcException>(() => _sut.ClockIn(request, _callContext));
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    #endregion

    #region ClockOut

    [Fact]
    public async Task ClockOut_Success_ReturnsAttendanceWithWorkHours()
    {
        const string employeeId = "emp-1";
        SetupEmployeeExists(employeeId);

        var openRecord = CreateDocument(employeeId, clockIn: DateTime.UtcNow.AddHours(-8).ToString("o"));
        var updatedRecord = CreateDocument(employeeId, clockIn: openRecord.ClockIn,
            clockOut: DateTime.UtcNow.ToString("o"), workHours: 8.0);

        _repoMock.Setup(r => r.GetOpenRecordAsync(employeeId))
            .ReturnsAsync(openRecord);
        _repoMock.Setup(r => r.ClockOutAsync(employeeId, It.IsAny<string>(), It.IsAny<double>()))
            .ReturnsAsync(updatedRecord);

        var request = new ClockOutRequest { EmployeeId = employeeId };

        var result = await _sut.ClockOut(request, _callContext);

        Assert.Equal(employeeId, result.EmployeeId);
        Assert.NotEmpty(result.ClockOut);
        _repoMock.Verify(r => r.ClockOutAsync(employeeId, It.IsAny<string>(), It.IsAny<double>()), Times.Once);
    }

    [Fact]
    public async Task ClockOut_NoOpenRecord_ThrowsNotFound()
    {
        const string employeeId = "emp-1";
        SetupEmployeeExists(employeeId);

        _repoMock.Setup(r => r.GetOpenRecordAsync(employeeId))
            .ReturnsAsync((AttendanceDocument?)null);

        var request = new ClockOutRequest { EmployeeId = employeeId };

        var ex = await Assert.ThrowsAsync<RpcException>(() => _sut.ClockOut(request, _callContext));
        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task ClockOut_EmptyEmployeeId_ThrowsInvalidArgument()
    {
        var request = new ClockOutRequest { EmployeeId = "" };

        var ex = await Assert.ThrowsAsync<RpcException>(() => _sut.ClockOut(request, _callContext));
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    #endregion

    #region GetAttendance

    [Fact]
    public async Task GetAttendance_Success_ReturnsAttendance()
    {
        var doc = CreateDocument();

        _repoMock.Setup(r => r.GetByIdAsync(doc.AttendanceId))
            .ReturnsAsync(doc);

        var request = new GetAttendanceRequest { AttendanceId = doc.AttendanceId };

        var result = await _sut.GetAttendance(request, _callContext);

        Assert.Equal(doc.AttendanceId, result.AttendanceId);
        Assert.Equal(doc.EmployeeId, result.EmployeeId);
        Assert.Equal(doc.Date, result.Date);
    }

    [Fact]
    public async Task GetAttendance_NotFound_ThrowsNotFound()
    {
        _repoMock.Setup(r => r.GetByIdAsync("nonexistent"))
            .ReturnsAsync((AttendanceDocument?)null);

        var request = new GetAttendanceRequest { AttendanceId = "nonexistent" };

        var ex = await Assert.ThrowsAsync<RpcException>(() => _sut.GetAttendance(request, _callContext));
        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task GetAttendance_EmptyId_ThrowsInvalidArgument()
    {
        var request = new GetAttendanceRequest { AttendanceId = "" };

        var ex = await Assert.ThrowsAsync<RpcException>(() => _sut.GetAttendance(request, _callContext));
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    #endregion

    #region ListAttendanceByPeriod

    [Fact]
    public async Task ListAttendanceByPeriod_Success_ReturnsRecords()
    {
        var docs = new List<AttendanceDocument>
        {
            CreateDocument(clockIn: "2024-01-15T09:00:00Z", clockOut: "2024-01-15T17:00:00Z", workHours: 8.0),
            CreateDocument(clockIn: "2024-01-16T09:00:00Z", clockOut: "2024-01-16T18:00:00Z", workHours: 9.0)
        };

        _repoMock.Setup(r => r.ListByPeriodAsync("emp-1", "2024-01-15", "2024-01-31", 10, null))
            .ReturnsAsync((docs.AsReadOnly(), "cursor-2", true));

        var request = new ListAttendanceByPeriodRequest
        {
            EmployeeId = "emp-1",
            StartDate = "2024-01-15",
            EndDate = "2024-01-31",
            Limit = 10
        };

        var result = await _sut.ListAttendanceByPeriod(request, _callContext);

        Assert.Equal(2, result.Records.Count);
        Assert.Equal("cursor-2", result.NextCursor);
        Assert.True(result.HasMore);
    }

    [Fact]
    public async Task ListAttendanceByPeriod_EmptyResults_ReturnsEmptyList()
    {
        _repoMock.Setup(r => r.ListByPeriodAsync("emp-1", "2024-06-01", "2024-06-30", 10, null))
            .ReturnsAsync((Array.Empty<AttendanceDocument>().AsReadOnly(), (string?)null, false));

        var request = new ListAttendanceByPeriodRequest
        {
            EmployeeId = "emp-1",
            StartDate = "2024-06-01",
            EndDate = "2024-06-30",
            Limit = 10
        };

        var result = await _sut.ListAttendanceByPeriod(request, _callContext);

        Assert.Empty(result.Records);
        Assert.Equal(string.Empty, result.NextCursor);
        Assert.False(result.HasMore);
    }

    [Fact]
    public async Task ListAttendanceByPeriod_MissingEmployeeId_ThrowsInvalidArgument()
    {
        var request = new ListAttendanceByPeriodRequest
        {
            EmployeeId = "",
            StartDate = "2024-01-01",
            EndDate = "2024-01-31"
        };

        var ex = await Assert.ThrowsAsync<RpcException>(() => _sut.ListAttendanceByPeriod(request, _callContext));
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    [Fact]
    public async Task ListAttendanceByPeriod_MissingStartDate_ThrowsInvalidArgument()
    {
        var request = new ListAttendanceByPeriodRequest
        {
            EmployeeId = "emp-1",
            StartDate = "",
            EndDate = "2024-01-31"
        };

        var ex = await Assert.ThrowsAsync<RpcException>(() => _sut.ListAttendanceByPeriod(request, _callContext));
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    #endregion
}
