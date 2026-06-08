using System.Collections.Concurrent;
using AttendanceService.Models;

namespace AttendanceService.Repositories;

/// <summary>
/// In-memory repository for local development without Cosmos DB.
/// </summary>
public class InMemoryAttendanceRepository : IAttendanceRepository
{
    private readonly ConcurrentDictionary<string, AttendanceDocument> _store = new();

    public Task<AttendanceDocument> ClockInAsync(AttendanceDocument document)
    {
        if (string.IsNullOrEmpty(document.Id))
            document.Id = Guid.NewGuid().ToString();
        if (string.IsNullOrEmpty(document.AttendanceId))
            document.AttendanceId = document.Id;
        _store[document.AttendanceId] = document;
        return Task.FromResult(document);
    }

    public Task<AttendanceDocument> ClockOutAsync(string employeeId, string clockOut, double workHours)
    {
        var open = _store.Values.FirstOrDefault(a => a.EmployeeId == employeeId && string.IsNullOrEmpty(a.ClockOut));
        if (open == null)
            throw new KeyNotFoundException($"No open attendance record for employee {employeeId}.");
        open.ClockOut = clockOut;
        open.WorkHours = workHours;
        return Task.FromResult(open);
    }

    public Task<AttendanceDocument?> GetOpenRecordAsync(string employeeId)
    {
        var doc = _store.Values.FirstOrDefault(a => a.EmployeeId == employeeId && string.IsNullOrEmpty(a.ClockOut));
        return Task.FromResult(doc);
    }

    public Task<AttendanceDocument?> GetByIdAsync(string attendanceId)
    {
        _store.TryGetValue(attendanceId, out var doc);
        return Task.FromResult(doc);
    }

    public Task<(IReadOnlyList<AttendanceDocument> Records, string? NextCursor, bool HasMore)> ListByPeriodAsync(
        string employeeId, string startDate, string endDate, int limit, string? cursor)
    {
        var query = _store.Values
            .Where(a => a.EmployeeId == employeeId)
            .Where(a => string.Compare(a.Date, startDate, StringComparison.Ordinal) >= 0)
            .Where(a => string.Compare(a.Date, endDate, StringComparison.Ordinal) <= 0)
            .OrderByDescending(a => a.Date)
            .ThenByDescending(a => a.ClockIn)
            .ToList();

        int startIndex = 0;
        if (!string.IsNullOrEmpty(cursor) && int.TryParse(cursor, out var idx))
            startIndex = idx;

        var page = query.Skip(startIndex).Take(limit).ToList();
        var hasMore = startIndex + limit < query.Count;
        string? nextCursor = hasMore ? (startIndex + limit).ToString() : null;

        return Task.FromResult<(IReadOnlyList<AttendanceDocument>, string?, bool)>((page, nextCursor, hasMore));
    }
}
