using Bastet.Data;
using Bastet.Services;
using Bastet.Services.Locking;
using Bastet.Services.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bastet.Controllers;

/// <summary>
/// Baseline authorization for every action across all SubnetController partials. Individual actions
/// apply stricter policies on top; because the role policies are cumulative (View is satisfied by
/// Edit, Delete or Admin), this baseline never rejects anyone the action itself would allow.
/// </summary>
[Authorize(Policy = "RequireViewRole")]
public partial class SubnetController(
    BastetDbContext context,
    IIpUtilityService ipUtilityService,
    ISubnetValidationService subnetValidationService,
    IHostIpValidationService hostIpValidationService,
    IUserContextService userContextService,
    ISubnetLockingService subnetLockingService,
    ILogger<SubnetController> logger) : Controller
{
}
