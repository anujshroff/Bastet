using Bastet.Services.Security;
using System.ComponentModel.DataAnnotations;

namespace Bastet.Models.ViewModels;

public class HostIpViewModel
{
    public string IP { get; set; } = string.Empty;
    public string? Name { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? LastModifiedAt { get; set; }
    public string? ModifiedBy { get; set; }
}

public class CreateHostIpViewModel
{
    [Required(ErrorMessage = "IP address is required")]
    [NetworkInput(RequireValidIp = true, ErrorMessage = "Invalid IP address format")]
    [SanitizeNetworkInput]
    [Display(Name = "IP Address")]
    public string IP { get; set; } = string.Empty;

    [StringLength(100, ErrorMessage = "Name cannot exceed 100 characters")]
    [NoHtml(ErrorMessage = "HTML tags are not allowed in host names")]
    [SafeText(ErrorMessage = "Host name contains invalid characters")]
    [SanitizeName]
    [Display(Name = "Host Name (Optional)")]
    public string? Name { get; set; }

    [Required]
    public int SubnetId { get; set; }

    public string SubnetInfo { get; set; } = string.Empty;
    public string NetworkAddress { get; set; } = string.Empty;
    public int Cidr { get; set; }
    public string SubnetRange { get; set; } = string.Empty;
}

public class EditHostIpViewModel
{
    [Required]
    public string IP { get; set; } = string.Empty;

    [StringLength(100, ErrorMessage = "Name cannot exceed 100 characters")]
    [NoHtml(ErrorMessage = "HTML tags are not allowed in host names")]
    [SafeText(ErrorMessage = "Host name contains invalid characters")]
    [SanitizeName]
    [Display(Name = "Host Name (Optional)")]
    public string? Name { get; set; }

    [Required]
    public int SubnetId { get; set; }

    public string SubnetInfo { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? LastModifiedAt { get; set; }

    [Required]
    public byte[] RowVersion { get; set; } = [];
}

public class DeleteHostIpViewModel
{
    public string IP { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string SubnetInfo { get; set; } = string.Empty;
    public int SubnetId { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
}
