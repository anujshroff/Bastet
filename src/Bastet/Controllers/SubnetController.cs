using Bastet.Data;
using Bastet.Services;
using Bastet.Services.Locking;
using Bastet.Services.Validation;
using Microsoft.AspNetCore.Mvc;

namespace Bastet.Controllers;

public partial class SubnetController(
    BastetDbContext context,
    IIpUtilityService ipUtilityService,
    ISubnetValidationService subnetValidationService,
    IHostIpValidationService hostIpValidationService,
    IUserContextService userContextService,
    ISubnetLockingService subnetLockingService) : Controller
{
}
