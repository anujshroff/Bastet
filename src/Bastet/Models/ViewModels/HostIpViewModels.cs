using Bastet.Services.Security;
using System.ComponentModel.DataAnnotations;

namespace Bastet.Models.ViewModels;

/// <summary>
/// Basic view model for host IP assignment
/// </summary>
public class HostIpViewModel
{
    public string IP { get; set; } = string.Empty;
    public string? Name { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? LastModifiedAt { get; set; }
    public string? ModifiedBy { get; set; }
}

/// <summary>
/// View model for creating a host IP assignment
/// </summary>
public class CreateHostIpViewModel
{
    [Required(ErrorMessage = "IP address is required")]
    [NetworkInput(RequireValidIp = true, ErrorMessage = "Invalid IP address format")]
    [Display(Name = "IP Address")]
    public string IP { get; set; } = string.Empty;

    [StringLength(100, ErrorMessage = "Name cannot exceed 100 characters")]
    [NoHtml(ErrorMessage = "HTML tags are not allowed in host names")]
    [SafeText(ErrorMessage = "Host name contains invalid characters")]
    [Display(Name = "Host Name (Optional)")]
    public string? Name { get; set; }

    [Required]
    public int SubnetId { get; set; }

    // For display only
    public string SubnetInfo { get; set; } = string.Empty;
    public string NetworkAddress { get; set; } = string.Empty;
    public int Cidr { get; set; }
    public string SubnetRange { get; set; } = string.Empty;
}

/// <summary>
/// View model for editing a host IP assignment
/// </summary>
public class EditHostIpViewModel
{
    [Required]
    public string IP { get; set; } = string.Empty; // Primary key, read-only

    [StringLength(100, ErrorMessage = "Name cannot exceed 100 characters")]
    [NoHtml(ErrorMessage = "HTML tags are not allowed in host names")]
    [SafeText(ErrorMessage = "Host name contains invalid characters")]
    [Display(Name = "Host Name (Optional)")]
    public string? Name { get; set; }

    [Required]
    public int SubnetId { get; set; } // Read-only

    // For display only
    public string SubnetInfo { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? LastModifiedAt { get; set; }

    // For concurrency control
    [Required]
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>
/// View model for host IP assignment deletion
/// </summary>
public class DeleteHostIpViewModel
{
    public string IP { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string SubnetInfo { get; set; } = string.Empty;
    public int SubnetId { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
}
