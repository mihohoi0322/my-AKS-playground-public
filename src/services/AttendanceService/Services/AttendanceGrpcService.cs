using AttendanceService.Models;
using AttendanceService.Repositories;
using Grpc.Core;
using HRSystem.Protos.Attendance.V1;
using HRSystem.Protos.Employee.V1;
using HRSystem.Shared.Audit;
using Microsoft.Extensions.Logging;

namespace AttendanceService.Services;

public sealed class AttendanceGrpcService : HRSystem.Protos.Attendance.V1.AttendanceService.AttendanceServiceBase
{
    private readonly IAttendanceRepository _repository;
    private readonly EmployeeService.EmployeeServiceClient _employeeClient;
    private readonly ILogger<AttendanceGrpcService> _logger;

    public AttendanceGrpcService(
        IAttendanceRepository repository,
        EmployeeService.EmployeeServiceClient employeeClient,
        ILogger<AttendanceGrpcService> logger)
    {
        _repository = repository;
        _employeeClient = employeeClient;
        _logger = logger;
    }

    [Audit(AuditEventType.AttendanceClockedIn)]
    public override async Task<Attendance> ClockIn(ClockInRequest request, ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.EmployeeId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "employee_id is required"));

        await ValidateEmployeeAsync(request.EmployeeId, context.CancellationToken);

        var now = DateTime.UtcNow;
        var attendanceId = Guid.NewGuid().ToString();
        var document = new AttendanceDocument
        {
            Id = attendanceId,
            AttendanceId = attendanceId,
            EmployeeId = request.EmployeeId,
            Date = DateOnly.FromDateTime(now).ToString("yyyy-MM-dd"),
            ClockIn = now.ToString("o"),
            ClockOut = string.Empty,
            WorkHours = 0,
            Type = string.IsNullOrWhiteSpace(request.Type) ? "regular" : request.Type
        };

        try
        {
            var created = await _repository.ClockInAsync(document);
            return ToProto(created);
        }
        catch (InvalidOperationException ex)
        {
            throw new RpcException(new Status(StatusCode.AlreadyExists, ex.Message));
        }
    }

    [Audit(AuditEventType.AttendanceClockedOut)]
    public override async Task<Attendance> ClockOut(ClockOutRequest request, ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.EmployeeId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "employee_id is required"));

        await ValidateEmployeeAsync(request.EmployeeId, context.CancellationToken);

        var now = DateTime.UtcNow;

        try
        {
            // Find today's open record to calculate work hours
            var openRecord = await _repository.GetOpenRecordAsync(request.EmployeeId)
                ?? throw new InvalidOperationException($"No open attendance record found for employee {request.EmployeeId} today");

            var workHours = 0.0;
            if (DateTime.TryParse(openRecord.ClockIn, out var clockInTime))
            {
                workHours = Math.Round((now - clockInTime).TotalHours, 2);
            }

            var updated = await _repository.ClockOutAsync(request.EmployeeId, now.ToString("o"), workHours);
            return ToProto(updated);
        }
        catch (InvalidOperationException ex)
        {
            throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
        }
    }

    [NoAudit("read-only query, not auditable")]
    public override async Task<Attendance> GetAttendance(GetAttendanceRequest request, ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.AttendanceId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "attendance_id is required"));

        var doc = await _repository.GetByIdAsync(request.AttendanceId);
        if (doc is null)
            throw new RpcException(new Status(StatusCode.NotFound, $"Attendance {request.AttendanceId} not found"));

        return ToProto(doc);
    }

    [NoAudit("read-only query, not auditable")]
    public override async Task<ListAttendanceByPeriodResponse> ListAttendanceByPeriod(
        ListAttendanceByPeriodRequest request, ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.EmployeeId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "employee_id is required"));
        if (string.IsNullOrWhiteSpace(request.StartDate))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "start_date is required"));
        if (string.IsNullOrWhiteSpace(request.EndDate))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "end_date is required"));

        var (records, nextCursor, hasMore) = await _repository.ListByPeriodAsync(
            request.EmployeeId, request.StartDate, request.EndDate, request.Limit,
            string.IsNullOrEmpty(request.Cursor) ? null : request.Cursor);

        var response = new ListAttendanceByPeriodResponse
        {
            NextCursor = nextCursor ?? string.Empty,
            HasMore = hasMore
        };
        response.Records.AddRange(records.Select(ToProto));

        return response;
    }

    private async Task ValidateEmployeeAsync(string employeeId, CancellationToken cancellationToken)
    {
        try
        {
            var employee = await _employeeClient.GetEmployeeAsync(
                new GetEmployeeRequest { EmployeeId = employeeId },
                cancellationToken: cancellationToken);

            if (string.Equals(employee.Status, "inactive", StringComparison.OrdinalIgnoreCase))
            {
                throw new RpcException(new Status(StatusCode.FailedPrecondition, $"Employee {employeeId} is inactive"));
            }
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"Employee {employeeId} not found"));
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable)
        {
            _logger.LogWarning("Employee service unavailable while validating {EmployeeId}", employeeId);
            throw new RpcException(new Status(StatusCode.Unavailable, "Employee service is unavailable"));
        }
        catch (RpcException)
        {
            throw; // re-throw other RpcExceptions (FailedPrecondition, etc.)
        }
    }

    private static Attendance ToProto(AttendanceDocument doc) => new()
    {
        AttendanceId = doc.AttendanceId,
        EmployeeId = doc.EmployeeId,
        Date = doc.Date,
        ClockIn = doc.ClockIn,
        ClockOut = doc.ClockOut,
        WorkHours = doc.WorkHours,
        Type = doc.Type
    };
}
