namespace Bastet.Models;

/// <summary>
/// Base entity containing common audit fields
/// </summary>
public abstract class BaseEntity
{
    public DateTime CreatedAt { get; set; }
    public DateTime? LastModifiedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? ModifiedBy { get; set; }
}
