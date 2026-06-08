using AttendanceService.Models;

namespace AttendanceService.Repositories;

public interface IAttendanceRepository
{
    Task<AttendanceDocument> ClockInAsync(AttendanceDocument document);
    Task<AttendanceDocument> ClockOutAsync(string employeeId, string clockOut, double workHours);
    Task<AttendanceDocument?> GetOpenRecordAsync(string employeeId);
    Task<AttendanceDocument?> GetByIdAsync(string attendanceId);
    Task<(IReadOnlyList<AttendanceDocument> Records, string? NextCursor, bool HasMore)> ListByPeriodAsync(
        string employeeId, string startDate, string endDate, int limit, string? cursor);
}
