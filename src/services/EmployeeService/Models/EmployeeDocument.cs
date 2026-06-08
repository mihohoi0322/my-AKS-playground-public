namespace EmployeeService.Models;

public class EmployeeDocument
{
    public string Id { get; set; } = string.Empty;
    public string EmployeeId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DepartmentId { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public string HireDate { get; set; } = string.Empty;
    public string Status { get; set; } = "active";
}
