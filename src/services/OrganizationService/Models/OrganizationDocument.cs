namespace OrganizationService.Models;

public class OrganizationDocument
{
    public string Id { get; set; } = string.Empty;
    public string OrgId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ParentOrgId { get; set; } = string.Empty;
    public int Level { get; set; }
    public string ManagerId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
