using EmployeeService.Models;

namespace EmployeeService.Repositories;

public interface IEmployeeRepository
{
    Task<EmployeeDocument> CreateAsync(EmployeeDocument document, CancellationToken cancellationToken = default);
    Task<EmployeeDocument?> GetAsync(string employeeId, CancellationToken cancellationToken = default);
    Task<EmployeeDocument> UpdateAsync(EmployeeDocument document, string etag, CancellationToken cancellationToken = default);
    Task<EmployeeDocument> DeleteAsync(string employeeId, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<EmployeeDocument> Items, string? ContinuationToken, bool HasMore)> ListAsync(
        int limit,
        string? cursor = null,
        string? status = null,
        string? departmentId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true when at least one employee belongs to the given department.
    /// Excludes logically-deleted (status="inactive") employees so that the existence
    /// check aligns with organization-deletion gating semantics (docs/db-design.md §6).
    /// </summary>
    Task<bool> HasByDepartmentAsync(string departmentId, CancellationToken cancellationToken = default);
}
