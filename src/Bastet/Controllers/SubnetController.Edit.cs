using Bastet.Models;
using Bastet.Models.ViewModels;
using Bastet.Services.Data;
using Bastet.Services.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bastet.Controllers;

public partial class SubnetController : Controller
{
    // GET: Subnet/Edit/5
    [Authorize(Policy = "RequireEditRole")]
    public async Task<IActionResult> Edit(int id)
    {
        Subnet? subnet = await context.Subnets
            .Include(s => s.ParentSubnet)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (subnet == null)
        {
            return this.RedirectToErrorPage(404, $"The subnet with ID {id} could not be found or may have been deleted.");
        }

        EditSubnetViewModel viewModel = new()
        {
            Id = subnet.Id,
            Name = subnet.Name,
            NetworkAddress = subnet.NetworkAddress,
            Cidr = subnet.Cidr,
            OriginalCidr = subnet.Cidr, // Store original CIDR for comparison
            Description = subnet.Description,
            Tags = subnet.Tags,
            SubnetMask = ipUtilityService.CalculateSubnetMask(subnet.Cidr),
            CreatedAt = subnet.CreatedAt,
            LastModifiedAt = subnet.LastModifiedAt,
            RowVersion = subnet.RowVersion,
            IsAzureLinked = !string.IsNullOrEmpty(subnet.AzureResourceId)
        };

        // Add parent subnet info if exists
        if (subnet.ParentSubnet != null)
        {
            viewModel.ParentSubnetInfo = $"{subnet.ParentSubnet.Name} ({subnet.ParentSubnet.NetworkAddress}/{subnet.ParentSubnet.Cidr})";
        }

        return View(viewModel);
    }

    // POST: Subnet/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = "RequireEditRole")]
    public async Task<IActionResult> Edit(int id, EditSubnetViewModel viewModel)
    {
        if (id != viewModel.Id)
        {
            return this.RedirectToErrorPage(404, "The ID in the URL doesn't match the ID in the form data.");
        }

        if (ModelState.IsValid)
        {
            try
            {
                // The global lock (not a per-subnet one): a CIDR change alters containment
                // relationships, so it must exclude concurrent creates/imports/deletes that
                // validate against this subnet. Same-subnet conflicts are caught by RowVersion.
                Subnet result = await subnetLockingService.ExecuteWithSubnetLockAsync(async () =>
                {
                    // Retrieve existing subnet with relations for validation
                    Subnet? subnet = await context.Subnets
                        .Include(s => s.ParentSubnet)
                        .FirstOrDefaultAsync(s => s.Id == id) ?? throw new InvalidOperationException($"The subnet with ID {id} could not be found or may have been deleted.");

                    // Load child subnets directly to avoid navigation property issues
                    List<Subnet> childSubnets = await context.Subnets
                        .Where(s => s.ParentSubnetId == id)
                        .ToListAsync();

                    // Check if CIDR has changed
                    bool cidrChanged = viewModel.Cidr != subnet.Cidr;

                    // An imported row records the prefix its Azure resource had at link time, and
                    // the reconciler compares Bastet's current prefix against Azure's, reading any
                    // difference as Azure-side drift. Changing the CIDR here breaks that invariant
                    // silently: the row becomes a deletion candidate reported as "no longer exists
                    // in Azure", and confirming it archives the subtree and its host IPs while the
                    // Azure resource is healthy. Nothing downstream can tell that state apart from a
                    // genuine Azure-side prefix change, so it has to be refused at the write.
                    if (cidrChanged && !string.IsNullOrEmpty(subnet.AzureResourceId))
                    {
                        throw new ValidationException(
                            "This subnet is linked to an Azure resource, so its CIDR cannot be changed here. " +
                            "Change the prefix in Azure and re-import, or delete the subnet and recreate it.");
                    }

                    // Always validate CIDR changes, regardless of whether this is a first or subsequent attempt
                    // This ensures validation is never bypassed, even on multiple form submissions
                    if (viewModel.Cidr != subnet.Cidr)
                    {
                        // Get siblings for validation if we have a parent
                        List<Subnet> siblings = [];
                        if (subnet.ParentSubnetId.HasValue)
                        {
                            siblings = await context.Subnets
                                .Where(s => s.ParentSubnetId == subnet.ParentSubnetId && s.Id != subnet.Id)
                                .ToListAsync();
                        }

                        // Get all other subnets for comprehensive overlap validation
                        List<Subnet> allOtherSubnets = await context.Subnets
                            .Where(s => s.Id != subnet.Id)
                            .ToListAsync();

                        // Always use the actual database value for original CIDR, not the viewModel value
                        // This prevents validation bypass on subsequent attempts
                        ValidationResult validationResult = subnetValidationService.ValidateSubnetCidrChange(
                            subnet.Id,
                            subnet.NetworkAddress,
                            subnet.Cidr, // Use actual DB value instead of viewModel.OriginalCidr
                            viewModel.Cidr,
                            subnet.ParentSubnet,
                            siblings,
                            childSubnets,
                            allOtherSubnets);

                        if (!validationResult.IsValid)
                        {
                            string errorMessage = string.Join("; ", validationResult.Errors.Select(e => e.Message));
                            throw new ValidationException($"CIDR validation failed: {errorMessage}");
                        }

                        // Validate host IPs on any CIDR change. An increase can move the broadcast
                        // address onto an assigned IP; a decrease from a /31 or /32 can reinstate
                        // the network address reservation under one. Both leave a row the create
                        // path refuses to produce, so neither direction can be skipped.
                        if (viewModel.Cidr != subnet.Cidr)
                        {
                            // Validate that all host IPs are still within the subnet range after CIDR change
                            ValidationResult hostIpValidationResult = hostIpValidationService.ValidateSubnetCidrChangeWithHostIps(
                                subnet.Id,
                                subnet.NetworkAddress,
                                subnet.Cidr,
                                viewModel.Cidr);

                            if (!hostIpValidationResult.IsValid)
                            {
                                string errorMessage = string.Join("; ", hostIpValidationResult.Errors.Select(e => e.Message));
                                throw new ValidationException($"Host IP validation failed: {errorMessage}");
                            }
                        }
                    }

                    // Update all editable properties including CIDR now
                    subnet.Name = viewModel.Name;
                    subnet.Description = viewModel.Description;
                    subnet.Tags = viewModel.Tags;
                    subnet.LastModifiedAt = DateTime.UtcNow;
                    subnet.ModifiedBy = userContextService.GetCurrentUsername();

                    if (cidrChanged)
                    {
                        subnet.Cidr = viewModel.Cidr;
                    }

                    // Set the original RowVersion for concurrency control
                    // This tells EF what the RowVersion was when the user started editing
                    context.Entry(subnet).OriginalValues["RowVersion"] = viewModel.RowVersion;

                    context.Subnets.Update(subnet);
                    await context.SaveChangesAsync();

                    return subnet;
                });

                TempData["SuccessMessage"] = $"Subnet '{result.Name}' was updated successfully.";
                return RedirectToAction(nameof(Details), new { id = result.Id });
            }
            catch (ValidationException ex)
            {
                // Handle validation errors
                ModelState.AddModelError("Cidr", ex.Message);
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!SubnetExists(id))
                {
                    return this.RedirectToErrorPage(404, "The subnet no longer exists. It may have been deleted by another user.");
                }

                // Handle concurrency conflict - reload current data and show user-friendly message.
                //
                // AsNoTracking is load-bearing, not a performance tweak. The failed save left this
                // subnet tracked and Modified, and UpdateAuditFields already re-stamped its
                // LastModifiedAt, so an ordinary tracking query resolves to that same dirty instance
                // and returns the caller's own rejected values as "current database values" - the
                // exact opposite of what the message below tells the user they are looking at.
                Subnet? currentSubnet = await context.Subnets
                    .AsNoTracking()
                    .Include(s => s.ParentSubnet)
                    .FirstOrDefaultAsync(s => s.Id == id);

                if (currentSubnet != null)
                {
                    // Update the view model with current database values for concurrency control
                    viewModel.RowVersion = currentSubnet.RowVersion;
                    viewModel.NetworkAddress = currentSubnet.NetworkAddress;
                    viewModel.OriginalCidr = currentSubnet.Cidr;
                    viewModel.CreatedAt = currentSubnet.CreatedAt;
                    viewModel.LastModifiedAt = currentSubnet.LastModifiedAt;

                    if (currentSubnet.ParentSubnet != null)
                    {
                        viewModel.ParentSubnetInfo = $"{currentSubnet.ParentSubnet.Name} ({currentSubnet.ParentSubnet.NetworkAddress}/{currentSubnet.ParentSubnet.Cidr})";
                    }

                    // Clear the RowVersion from ModelState so the form field uses the updated model value
                    ModelState.Remove(nameof(viewModel.RowVersion));
                }

                ModelState.AddModelError("",
                    "This subnet was modified by another user while you were editing it. " +
                    "Your changes have been preserved below, but you should review the current values before saving. " +
                    "Click 'Save Changes' again to apply your updates.");
            }
            catch (TimeoutException)
            {
                ModelState.AddModelError("", "The operation timed out due to high concurrency. Please try again.");
            }
            catch (Exception ex) when (SqlSaveOutcome.IsIndeterminate(ex))
            {
                // The server may already have committed - see SqlSaveOutcome. Saying "error" here
                // told an operator their change had not happened while the row carried it.
                logger.LogError(ex, "Subnet edit outcome unknown for subnet {SubnetId}", id);
                ModelState.AddModelError("",
                    "BASTET could not confirm whether this change was applied. "
                    + "Reload the subnet to see its current state before retrying.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Subnet edit failed for subnet {SubnetId}", id);
                ModelState.AddModelError("", "Error updating subnet. Details have been logged.");
            }
        }

        // If we got this far, something failed - repopulate the view model and return to the form.
        // AsNoTracking for the same reason as the concurrency handler above: this runs last and is
        // what actually reaches the view, so fixing only that one would change nothing on screen.
        // Nothing here is saved - every field below is display or the concurrency token.
        Subnet? origSubnet = await context.Subnets
            .AsNoTracking()
            .Include(s => s.ParentSubnet)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (origSubnet == null)
        {
            return this.RedirectToErrorPage(404, $"The subnet with ID {id} could not be found or may have been deleted.");
        }

        // Repopulate the display-only properties
        viewModel.NetworkAddress = origSubnet.NetworkAddress;

        // Re-derived from the database rather than trusted from the post, so a caller cannot claim a
        // row is unlinked to get the editable field back on a re-render.
        viewModel.IsAzureLinked = !string.IsNullOrEmpty(origSubnet.AzureResourceId);

        // Always set original CIDR to the actual DB value to prevent validation bypass
        viewModel.OriginalCidr = origSubnet.Cidr;

        // Update the subnet mask based on user's input CIDR value.
        //
        // This runs after a failed ModelState, which is exactly when Cidr can be a value
        // CalculateSubnetMask refuses - [Range(0,32)] is what made ModelState invalid, and this code
        // sits outside the try/catch above, so throwing here loses the whole form to an error page
        // instead of redisplaying it with the range message. The posted value is deliberately not
        // clamped: it is redisplayed, and rewriting what the operator typed would hide the mistake.
        // Same guard the Create action applies to a prefilled CIDR from the query string.
        bool hasUsableCidr = viewModel.Cidr is >= 0 and <= 32;

        if (!ModelState.IsValid || viewModel.Cidr != origSubnet.Cidr)
        {
            viewModel.SubnetMask = hasUsableCidr
                ? ipUtilityService.CalculateSubnetMask(viewModel.Cidr)
                : string.Empty;
        }
        else
        {
            viewModel.Cidr = origSubnet.Cidr;
            viewModel.OriginalCidr = origSubnet.Cidr;
            viewModel.SubnetMask = ipUtilityService.CalculateSubnetMask(origSubnet.Cidr);
        }

        viewModel.CreatedAt = origSubnet.CreatedAt;
        viewModel.LastModifiedAt = origSubnet.LastModifiedAt;
        // Ensure RowVersion is updated for concurrency control
        viewModel.RowVersion = origSubnet.RowVersion;

        // ...and clear the POSTED token out of ModelState, or the tag helper re-renders that one and
        // the assignment above changes nothing on screen. Only the concurrency catch used to do
        // this, so every other failure path redisplayed a stale token: the operator was told the
        // save failed, clicked Save again, and got "modified by another user" - about their own
        // write. It belongs here rather than in each catch because this block runs last on every
        // failure path and is what actually reaches the view.
        ModelState.Remove(nameof(viewModel.RowVersion));

        if (origSubnet.ParentSubnet != null)
        {
            viewModel.ParentSubnetInfo = $"{origSubnet.ParentSubnet.Name} ({origSubnet.ParentSubnet.NetworkAddress}/{origSubnet.ParentSubnet.Cidr})";
        }

        return View(viewModel);
    }
}

// Custom validation exception for cleaner error handling
public class ValidationException(string message) : Exception(message)
{
}
