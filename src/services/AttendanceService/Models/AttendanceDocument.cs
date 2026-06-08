using System.Text.Json.Serialization;

namespace AttendanceService.Models;

public class AttendanceDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("attendanceId")]
    public string AttendanceId { get; set; } = string.Empty;

    /// <summary>Cosmos DB partition key (/employeeId)</summary>
    [JsonPropertyName("employeeId")]
    public string EmployeeId { get; set; } = string.Empty;

    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;

    [JsonPropertyName("clockIn")]
    public string ClockIn { get; set; } = string.Empty;

    [JsonPropertyName("clockOut")]
    public string ClockOut { get; set; } = string.Empty;

    [JsonPropertyName("workHours")]
    public double WorkHours { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
}
