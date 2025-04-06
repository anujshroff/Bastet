namespace Bastet.Models.DTOs;

/// <summary>
/// DTO for host IP assignment data
/// </summary>
public class HostIpDto
{
    public string IP { get; set; } = string.Empty;
    public string? Name { get; set; }
    public int SubnetId { get; set; }
    public string SubnetName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? LastModifiedAt { get; set; }
    public string? ModifiedBy { get; set; }
}
