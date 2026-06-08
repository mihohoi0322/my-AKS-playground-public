using AttendanceService.Models;
using HRSystem.Shared.Cosmos;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace AttendanceService.Repositories;

public sealed class CosmosAttendanceRepository : IAttendanceRepository
{
    private readonly Container _container;
    private readonly ILogger<CosmosAttendanceRepository> _logger;

    public CosmosAttendanceRepository(ICosmosClientFactory cosmosClientFactory, CosmosSettings settings, ILogger<CosmosAttendanceRepository> logger)
    {
        _logger = logger;
        var client = cosmosClientFactory.CreateClient();
        _container = client.GetContainer(settings.DatabaseName, "attendance");
    }

    public async Task<AttendanceDocument> ClockInAsync(AttendanceDocument document)
    {
        // Check no existing record for today
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.employeeId = @empId AND c.date = @date")
            .WithParameter("@empId", document.EmployeeId)
            .WithParameter("@date", document.Date);

        using var feed = _container.GetItemQueryIterator<AttendanceDocument>(
            query,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(document.EmployeeId) });

        if (feed.HasMoreResults)
        {
            var page = await feed.ReadNextAsync();
            if (page.Count > 0)
            {
                throw new InvalidOperationException($"Attendance record already exists for employee {document.EmployeeId} on {document.Date}");
            }
        }

        var response = await _container.CreateItemAsync(document, new PartitionKey(document.EmployeeId));
        _logger.LogInformation("ClockIn created for employee {EmployeeId}, attendance {AttendanceId}", document.EmployeeId, document.AttendanceId);
        return response.Resource;
    }

    public async Task<AttendanceDocument?> GetOpenRecordAsync(string employeeId)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.employeeId = @empId AND c.date = @date AND c.clockOut = ''")
            .WithParameter("@empId", employeeId)
            .WithParameter("@date", today);

        using var feed = _container.GetItemQueryIterator<AttendanceDocument>(
            query,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(employeeId) });

        if (feed.HasMoreResults)
        {
            var page = await feed.ReadNextAsync();
            return page.FirstOrDefault();
        }

        return null;
    }

    public async Task<AttendanceDocument> ClockOutAsync(string employeeId, string clockOut, double workHours)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.employeeId = @empId AND c.date = @date AND c.clockOut = ''")
            .WithParameter("@empId", employeeId)
            .WithParameter("@date", today);

        using var feed = _container.GetItemQueryIterator<AttendanceDocument>(
            query,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(employeeId) });

        AttendanceDocument? existing = null;
        if (feed.HasMoreResults)
        {
            var page = await feed.ReadNextAsync();
            existing = page.FirstOrDefault();
        }

        if (existing is null)
        {
            throw new InvalidOperationException($"No open attendance record found for employee {employeeId} today");
        }

        existing.ClockOut = clockOut;
        existing.WorkHours = workHours;

        var response = await _container.ReplaceItemAsync(existing, existing.Id, new PartitionKey(employeeId));
        _logger.LogInformation("ClockOut updated for employee {EmployeeId}, attendance {AttendanceId}", employeeId, existing.AttendanceId);
        return response.Resource;
    }

    public async Task<AttendanceDocument?> GetByIdAsync(string attendanceId)
    {
        // attendanceId is the document id but PK is employeeId — cross-partition query needed
        var query = new QueryDefinition("SELECT * FROM c WHERE c.attendanceId = @id")
            .WithParameter("@id", attendanceId);

        using var feed = _container.GetItemQueryIterator<AttendanceDocument>(query);

        if (feed.HasMoreResults)
        {
            var page = await feed.ReadNextAsync();
            return page.FirstOrDefault();
        }

        return null;
    }

    public async Task<(IReadOnlyList<AttendanceDocument> Records, string? NextCursor, bool HasMore)> ListByPeriodAsync(
        string employeeId, string startDate, string endDate, int limit, string? cursor)
    {
        var effectiveLimit = limit > 0 ? limit : 20;

        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.employeeId = @empId AND c.date >= @start AND c.date <= @end ORDER BY c.date DESC")
            .WithParameter("@empId", employeeId)
            .WithParameter("@start", startDate)
            .WithParameter("@end", endDate);

        var options = new QueryRequestOptions
        {
            PartitionKey = new PartitionKey(employeeId),
            MaxItemCount = effectiveLimit
        };

        using var feed = _container.GetItemQueryIterator<AttendanceDocument>(query, continuationToken: string.IsNullOrEmpty(cursor) ? null : cursor, requestOptions: options);

        var records = new List<AttendanceDocument>();
        string? nextCursor = null;

        if (feed.HasMoreResults)
        {
            var page = await feed.ReadNextAsync();
            records.AddRange(page);
            nextCursor = page.ContinuationToken;
        }

        return (records, nextCursor, !string.IsNullOrEmpty(nextCursor));
    }
}
