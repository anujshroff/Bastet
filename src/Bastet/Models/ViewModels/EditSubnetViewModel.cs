using Bastet.Services.Security;
using System.ComponentModel.DataAnnotations;

namespace Bastet.Models.ViewModels;

public class EditSubnetViewModel
{

    public int Id { get; set; }

    [Display(Name = "Network Address")]
    public string NetworkAddress { get; set; } = string.Empty;

    [Required(ErrorMessage = "CIDR is required")]
    [Range(0, 32, ErrorMessage = "CIDR must be between 0 and 32")]
    [Display(Name = "CIDR")]
    public int Cidr { get; set; }

    public int OriginalCidr { get; set; }

    [Required(ErrorMessage = "Name is required")]
    [StringLength(100, ErrorMessage = "Name cannot be longer than 100 characters")]
    [NoHtml(ErrorMessage = "HTML tags are not allowed in subnet names")]
    [SanitizeName]
    [Display(Name = "Subnet Name")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Description")]
    [StringLength(1000, ErrorMessage = "Description cannot be longer than 1000 characters")]
    [NoHtml(ErrorMessage = "HTML tags are not allowed in descriptions")]
    [SanitizeDescription]
    public string? Description { get; set; }

    [Display(Name = "Tags")]
    [StringLength(255, ErrorMessage = "Tags cannot be longer than 255 characters")]
    [Bastet.Services.Security.Tags(MaxTags = 10, MaxTagLength = 50, ErrorMessage = "Invalid tags format")]
    [SanitizeTags]
    public string? Tags { get; set; }

    [Display(Name = "Subnet Mask")]
    public string SubnetMask { get; set; } = string.Empty;

    [Display(Name = "Parent Subnet")]
    public string? ParentSubnetInfo { get; set; }

    public bool IsAzureLinked { get; set; }

    [Display(Name = "Created")]
    public DateTime CreatedAt { get; set; }

    [Display(Name = "Last Modified")]
    public DateTime? LastModifiedAt { get; set; }

    [Timestamp]
    public byte[]? RowVersion { get; set; }
}
