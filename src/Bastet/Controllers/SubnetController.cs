using Bastet.Data;
using Bastet.Services;
using Bastet.Services.Locking;
using Bastet.Services.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bastet.Controllers;

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
