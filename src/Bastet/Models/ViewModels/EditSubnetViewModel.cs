using Bastet.Services.Security;
using System.ComponentModel.DataAnnotations;

namespace Bastet.Models.ViewModels;

/// <summary>
/// View model for editing subnet metadata (non-network properties)
/// </summary>
public class EditSubnetViewModel
{
    /// <summary>
    /// The subnet ID (hidden field)
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// The network address (display only)
    /// </summary>
    [Display(Name = "Network Address")]
    public string NetworkAddress { get; set; } = string.Empty;

    /// <summary>
    /// The CIDR notation (editable)
    /// </summary>
    [Required(ErrorMessage = "CIDR is required")]
    [Range(0, 32, ErrorMessage = "CIDR must be between 0 and 32")]
    [Display(Name = "CIDR")]
    public int Cidr { get; set; }

    /// <summary>
    /// The original CIDR value (for validation purposes)
    /// </summary>
    public int OriginalCidr { get; set; }

    // Editable properties

    // The [NoHtml] and [Tags] rules below are not decoration: sanitization runs *after* validation,
    // so without them the sanitizer silently rewrites a value this model has already accepted.
    // StripHtml can empty a name outright, defeating [Required], and SanitizeTags drops over-long
    // tags and everything past the tenth - all reported to the user as a successful update. These
    // must stay in step with CreateSubnetViewModel, which writes the same three columns.
    [Required(ErrorMessage = "Name is required")]
    [StringLength(100, ErrorMessage = "Name cannot be longer than 100 characters")]
    [NoHtml(ErrorMessage = "HTML tags are not allowed in subnet names")]
    [SanitizeName] // Auto-sanitization
    [Display(Name = "Subnet Name")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Description")]
    [StringLength(1000, ErrorMessage = "Description cannot be longer than 1000 characters")]
    [NoHtml(ErrorMessage = "HTML tags are not allowed in descriptions")]
    [SanitizeDescription] // Auto-sanitization
    public string? Description { get; set; }

    [Display(Name = "Tags")]
    [StringLength(255, ErrorMessage = "Tags cannot be longer than 255 characters")]
    [Bastet.Services.Security.Tags(MaxTags = 10, MaxTagLength = 50, ErrorMessage = "Invalid tags format")]
    [SanitizeTags] // Auto-sanitization
    public string? Tags { get; set; }

    // Additional display-only properties
    [Display(Name = "Subnet Mask")]
    public string SubnetMask { get; set; } = string.Empty;

    [Display(Name = "Parent Subnet")]
    public string? ParentSubnetInfo { get; set; }

    [Display(Name = "Created")]
    public DateTime CreatedAt { get; set; }

    [Display(Name = "Last Modified")]
    public DateTime? LastModifiedAt { get; set; }

    /// <summary>
    /// For concurrency control
    /// </summary>
    [Timestamp]
    public byte[]? RowVersion { get; set; }
}
