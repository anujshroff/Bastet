using Bastet.Models;
using Bastet.Models.ViewModels;
using Bastet.Services;
using Bastet.Services.Security;
using Bastet.Services.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bastet.Controllers;

public partial class SubnetController : Controller
{
    /// <summary>
    /// Maximum length for <see cref="Models.Subnet.Name"/>; matches the [MaxLength(100)] attribute on
    /// the entity, and the same limit the bulk import planner applies to Azure-derived names.
    /// </summary>
    private const int MaxSubnetNameLength = 100;

    /// <summary>
    /// Maximum length for <see cref="Models.Subnet.Description"/>; matches the [MaxLength(1000)]
    /// attribute on the entity.
    /// </summary>
    private const int MaxSubnetDescriptionLength = 1000;

    /// <summary>
    /// Maximum length for <see cref="Models.Subnet.AzureResourceId"/>; matches the [MaxLength(500)]
    /// attribute on the entity.
    /// </summary>
    private const int MaxAzureResourceIdLength = 500;

    /// <summary>
    /// Shapes a batch-create failure for whoever actually posted it.
    /// </summary>
    /// <remarks>
    /// The Azure import wizard submits <c>#import-form</c> as an ordinary full-page POST, unlike the
    /// bulk and reconcile wizards which post via AJAX and render errors inline. Returning
    /// <c>BadRequest(ModelState)</c> or a bare status code to a full-page navigation replaces the
    /// wizard with a serialized error body - and because UseStatusCodePagesWithReExecute skips
    /// responses that already have a body or content type, not even the error page steps in. The
    /// admin is left on a URL showing raw JSON, and Back restores the wizard from bfcache with its
    /// button still reading "Importing...". So an import redirects to the parent's Details page
    /// carrying the message, which is a proper PRG and matches the success path. Direct callers
    /// using this as a JSON API keep the status codes they already rely on.
    /// </remarks>
    private IActionResult BatchCreateFailure(bool isAzureImport, int parentId, string message, IActionResult apiResult)
    {
        if (!isAzureImport)
        {
            return apiResult;
        }

        TempData["ErrorMessage"] = message;
        return RedirectToAction("Details", new { id = parentId });
    }

    /// <summary>Every current ModelState error, flattened for a TempData message.</summary>
    private string ModelStateMessage(string fallback) =>
        ModelState.Values
            .SelectMany(v => v.Errors)
            .Select(e => e.ErrorMessage)
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .ToList() is { Count: > 0 } messages
                ? string.Join(" ", messages)
                : fallback;

    /// <summary>
    /// True when a client-supplied Azure resource ID is too long for the column.
    /// </summary>
    /// <remarks>
    /// Sanitization only trims these at 1000, so an over-long value would reach the insert and fail it
    /// with a generic error. Real ARM IDs run to roughly 330 characters, so anything longer is a
    /// crafted or broken post and is rejected rather than truncated: this value is an identifier, and
    /// reconcile matches Bastet subnets to live Azure resources by it. A truncated ID matches nothing,
    /// which would leave the subnet permanently reported as deleted in Azure - and reconcile offers
    /// exactly those for deletion.
    /// </remarks>
    private static bool IsAzureResourceIdTooLong(string? resourceId) =>
        resourceId?.Length > MaxAzureResourceIdLength;

    /// <summary>
    /// Builds the description for a subnet an Azure import has just marked fully allocated. The note
    /// is only appended when it fits: descriptions are capped, the note repeats what the
    /// IsFullyAllocated flag already records, and overflowing the column fails the insert and rolls
    /// back the entire import behind a generic error. Existing text is never sacrificed for the note.
    /// </summary>
    private static string AppendFullyAllocatedNote(string? existingDescription, string? azureSubnetName)
    {
        string note = $"Fully allocated by Azure subnet '{azureSubnetName}' which encompasses the entire address space.";

        if (string.IsNullOrEmpty(existingDescription))
        {
            return Truncate(note);
        }

        string combined = $"{existingDescription}\n{note}";
        return combined.Length <= MaxSubnetDescriptionLength
            ? combined
            : Truncate(existingDescription);

        static string Truncate(string value) =>
            value.Length > MaxSubnetDescriptionLength ? value[..MaxSubnetDescriptionLength] : value;
    }

    // POST: Subnet/BatchCreateChildSubnets
    /// <param name="isAzureImport">
    /// True when called from the Azure import wizard, which additionally renames the parent to the
    /// VNet name, stamps its resource ID, and redirects to Details instead of returning JSON.
    /// This used to be inferred from the Referer header, which is client-supplied: it could be
    /// forged, and a browser that strips it silently disabled the rename. Defaults to false, so
    /// callers using this as a plain batch-create API keep their existing JSON behaviour.
    /// </param>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = "RequireAdminRole")]
    public async Task<IActionResult> BatchCreateChildSubnets(int parentId, List<AzureImportSubnetViewModel> subnets, string? vnetName = null, string? vnetResourceId = null, bool isAzureImport = false, [FromServices] IInputSanitizationService? sanitizationService = null)
    {
        // Note on name length: the import wizard trims Azure names to Subnet.Name's limit before
        // posting them, so the length rule inherited from CreateSubnetViewModel is never the thing
        // that fails a real import - it is the guard for a caller posting directly. Trimming here
        // instead would mean clearing the binder's errors for these fields, which would also drop the
        // HTML and safe-text errors on the same field and make those rules apply only to short names.
        if (!ModelState.IsValid)
        {
            return BatchCreateFailure(isAzureImport, parentId,
                ModelStateMessage("The submitted subnets were not valid."), BadRequest(ModelState));
        }

        // Nothing bound means nothing was selected, or the post was malformed. Without this the
        // import would fall through to the parent rename below and report "imported 0 child
        // subnets" as a success, which reads as though the selection was honoured.
        if (subnets is null or { Count: 0 })
        {
            ModelState.AddModelError("subnets", "No subnets were submitted for import.");
            return BatchCreateFailure(isAzureImport, parentId,
                "No subnets were submitted for import.", BadRequest(ModelState));
        }

        // The one Azure write path with no feature-flag guard, while its eleven siblings all have one.
        // Gating on isAzureImport alone would not close it: the child stamp below is behind no flag at
        // all, so an Admin could still create Azure-linked rows with the feature off - rows the
        // Details page then renders a live "View in Azure Portal" link from, and which arm themselves
        // the moment the flag is enabled. So the test is on the Azure state being written, whatever
        // isAzureImport claims.
        //
        // Note this narrows the documented non-Azure JSON API: a caller using this as a plain
        // batch-create may no longer send AzureResourceId or vnetResourceId while the feature is off.
        // Sending them was never meaningful in that configuration.
        bool writesAzureState = isAzureImport
            || !string.IsNullOrEmpty(vnetResourceId)
            || subnets.Exists(s => !string.IsNullOrEmpty(s.AzureResourceId));

        if (writesAzureState && !AzureController.IsAzureImportEnabled())
        {
            return BatchCreateFailure(isAzureImport, parentId,
                "Azure Import feature is not enabled.",
                StatusCode(403, new { success = false, error = "Azure Import feature is not enabled" }));
        }

        // Sanitize user inputs before processing
        if (sanitizationService != null)
        {
            foreach (AzureImportSubnetViewModel subnet in subnets)
            {
                subnet.Name = sanitizationService.SanitizeName(subnet.Name);
                subnet.NetworkAddress = sanitizationService.SanitizeNetworkInput(subnet.NetworkAddress);
                subnet.Description = sanitizationService.SanitizeDescription(subnet.Description);
                subnet.Tags = sanitizationService.SanitizeTags(subnet.Tags);
                if (!string.IsNullOrEmpty(subnet.AzureResourceId))
                {
                    subnet.AzureResourceId = sanitizationService.SanitizeDescription(subnet.AzureResourceId);
                }
            }

            // Also sanitize vnetName if provided
            if (!string.IsNullOrEmpty(vnetName))
            {
                vnetName = sanitizationService.SanitizeName(vnetName);
            }

            if (!string.IsNullOrEmpty(vnetResourceId))
            {
                vnetResourceId = sanitizationService.SanitizeDescription(vnetResourceId);
            }
        }

        // Checked after sanitization, since that is what sets the final length (see the remarks on
        // IsAzureResourceIdTooLong for why these are rejected rather than trimmed to fit).
        if (subnets.Exists(s => IsAzureResourceIdTooLong(s.AzureResourceId)) || IsAzureResourceIdTooLong(vnetResourceId))
        {
            string tooLong = $"An Azure resource ID is longer than {MaxAzureResourceIdLength} characters and cannot be stored.";
            ModelState.AddModelError("subnets", tooLong);
            return BatchCreateFailure(isAzureImport, parentId, tooLong, BadRequest(ModelState));
        }

        // A fully-encompassing entry is never created as a child - its whole purpose is to rename the
        // parent and mark it fully allocated, and both of those writes live behind the Azure-import
        // guard. Without an import context there is nothing left for such an entry to do: child
        // creation is skipped because the entry exists, the parent writes are skipped because the
        // import flags are absent, and the transaction commits having written nothing while the
        // success message still announces a rename that never happened. Refuse the combination
        // instead of committing a no-op. Checked after sanitization, which is what decides whether
        // vnetName is really empty.
        if (subnets.Exists(s => s.FullyEncompassesVNetPrefix) && (!isAzureImport || string.IsNullOrEmpty(vnetName)))
        {
            ModelState.AddModelError("subnets",
                "A subnet marked as fully encompassing the VNet prefix can only be submitted as part of an Azure "
                + "import, which requires isAzureImport to be set and a vnetName to be supplied. It marks the parent "
                + "fully allocated rather than being created as a child, so on its own it would import nothing.");
            return BatchCreateFailure(isAzureImport, parentId,
                ModelStateMessage("The import could not be applied."), BadRequest(ModelState));
        }

        // The same entry cannot coexist with ordinary children either. It marks the parent fully
        // allocated, and the creation loop is skipped wholesale whenever one is present - so every
        // other subnet in the post is discarded, silently, under a success message. They cannot be
        // added afterwards either, because a fully-allocated parent refuses children. Azure cannot
        // produce this selection (subnets within a VNet may not overlap), so reaching it means a
        // crafted or corrupted post; refusing it matches what the bulk import planner already does
        // with the same shape.
        if (subnets.Count > 1 && subnets.Exists(s => s.FullyEncompassesVNetPrefix))
        {
            ModelState.AddModelError("subnets",
                $"A subnet marked as fully encompassing the VNet prefix covers the whole of the parent, so nothing "
                + $"can be created inside it, but {subnets.Count - 1} other subnet(s) were submitted with it. "
                + "Submit the encompassing subnet on its own, or submit the others without it.");
            return BatchCreateFailure(isAzureImport, parentId,
                ModelStateMessage("The import could not be applied."), BadRequest(ModelState));
        }

        try
        {
            // Validation reads and writes must happen under the global lock, or a concurrent
            // create/import could pass overlap validation against a tree this batch is changing.
            return await subnetLockingService.ExecuteWithSubnetLockAsync(() =>
                BatchCreateChildSubnetsCore(parentId, subnets, vnetName, vnetResourceId, isAzureImport));
        }
        catch (TimeoutException)
        {
            const string busy = "The operation timed out because another subnet operation is in progress. Please try again.";
            return BatchCreateFailure(isAzureImport, parentId, busy, StatusCode(503, busy));
        }
    }

    /// <summary>
    /// The Bastet name each posted row will be created under, keyed by its index in
    /// <paramref name="subnets"/>.
    /// </summary>
    /// <remarks>
    /// An Azure subnet owning several IPv4 prefixes posts one row per prefix, all carrying the same
    /// Azure name, and <c>Subnet.Name</c> has a NON-unique index - so they would persist as rows
    /// indistinguishable by name in every list and dropdown. Each such row is named for the range it
    /// holds. The same is done for any other duplicate name in the batch.
    ///
    /// Settled server-side because the browser is not the authority: a crafted or replayed post
    /// carries whatever names it likes. A row contributing no duplicate keeps its name exactly as
    /// posted, so ordinary single-prefix imports are unchanged.
    /// </remarks>
    private static Dictionary<int, string> ResolveImportNames(List<AzureImportSubnetViewModel> subnets)
    {
        HashSet<string> multiPrefixResourceIds = [.. subnets
            .Where(s => !s.FullyEncompassesVNetPrefix && !string.IsNullOrEmpty(s.AzureResourceId))
            .GroupBy(s => s.AzureResourceId!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)];

        Dictionary<int, string> names = [];
        HashSet<string> used = new(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < subnets.Count; i++)
        {
            AzureImportSubnetViewModel subnet = subnets[i];
            if (subnet.FullyEncompassesVNetPrefix)
            {
                continue;
            }

            string name = subnet.Name;
            bool sharesAnAzureSubnet = !string.IsNullOrEmpty(subnet.AzureResourceId)
                && multiPrefixResourceIds.Contains(subnet.AzureResourceId);

            if (sharesAnAzureSubnet || used.Contains(name))
            {
                // {NetworkAddress, Cidr} is unique across a batch - overlap validation refuses a
                // repeat - so a prefix-qualified name cannot collide with another one.
                name = SubnetNaming.WithSuffix(
                    name, $" ({subnet.NetworkAddress}/{subnet.Cidr})", MaxSubnetNameLength);
            }

            used.Add(name);
            names[i] = name;
        }

        return names;
    }

    private async Task<IActionResult> BatchCreateChildSubnetsCore(int parentId, List<AzureImportSubnetViewModel> subnets, string? vnetName, string? vnetResourceId, bool isAzureImport)
    {
        // Begin transaction
        using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();

        try
        {
            // Get the parent subnet first - validate early
            Subnet? parentSubnet = await context.Subnets.FindAsync(parentId);
            if (parentSubnet == null)
            {
                await transaction.RollbackAsync();
                string missing = $"Parent subnet with ID {parentId} not found";
                if (isAzureImport)
                {
                    // Details of a parent that does not exist would 404 in its own right, so the
                    // one failure that cannot redirect there goes to the subnet list instead.
                    TempData["ErrorMessage"] = missing;
                    return RedirectToAction("Index");
                }

                return NotFound(missing);
            }

            List<int> createdSubnetIds = [];
            AzureImportSubnetViewModel? fullyEncompassingSubnet = null;

            // One read of the tree for the whole batch. Every validation below works from this list,
            // and each row created inside the batch is appended to it, so an entry that overlaps an
            // earlier entry is still caught.
            List<Subnet> treeCache = await LoadSubnetTreeForBatchAsync();

            // Assign the parent and pick out the encompassing entry. Validation happens in the
            // creation loop below rather than here: that pass sees rows created earlier in the same
            // batch, so it catches everything a pass here would and more, and a failure anywhere
            // rolls the whole transaction back either way. Validating twice doubled the cost of the
            // batch for nothing.
            foreach (AzureImportSubnetViewModel subnet in subnets)
            {
                // Ensure parent ID is set correctly
                subnet.ParentSubnetId = parentId;

                // Check if this subnet fully encompasses a VNet address prefix
                if (subnet.FullyEncompassesVNetPrefix)
                {
                    fullyEncompassingSubnet = subnet;
                }
            }

            bool hasFullyEncompassingSubnet = fullyEncompassingSubnet != null;
            string? fullyEncompassingSubnetName = fullyEncompassingSubnet?.Name;

            // An encompassing entry is never created, so it skips the creation checks above - but it
            // still drives a write to the parent, so the parent has to be validated for it. Without
            // this, a caller could mark a parent that has children or host IPs as fully allocated (a
            // state SetAllocationStatus and the bulk planner both forbid), or claim any unrelated
            // prefix encompasses it.
            if (fullyEncompassingSubnet != null)
            {
                if (!string.Equals(fullyEncompassingSubnet.NetworkAddress, parentSubnet.NetworkAddress, StringComparison.Ordinal)
                    || fullyEncompassingSubnet.Cidr != parentSubnet.Cidr)
                {
                    ModelState.AddModelError("subnets",
                        $"Subnet '{fullyEncompassingSubnet.Name}' ({fullyEncompassingSubnet.NetworkAddress}/{fullyEncompassingSubnet.Cidr}) " +
                        $"does not cover the whole of {parentSubnet.NetworkAddress}/{parentSubnet.Cidr} and cannot mark it fully allocated.");
                    await transaction.RollbackAsync();
                    return BatchCreateFailure(isAzureImport, parentId,
                        ModelStateMessage("The import could not be applied."), BadRequest(ModelState));
                }

                ValidationResult allocationValidation = hostIpValidationService.ValidateSubnetCanBeFullyAllocated(parentId);
                if (!allocationValidation.IsValid)
                {
                    foreach (ValidationError error in allocationValidation.Errors)
                    {
                        ModelState.AddModelError("subnets", error.Message);
                    }

                    await transaction.RollbackAsync();
                    return BatchCreateFailure(isAzureImport, parentId,
                        ModelStateMessage("The import could not be applied."), BadRequest(ModelState));
                }
            }

            // Update parent subnet if this is an Azure import
            if (!string.IsNullOrEmpty(vnetName) && isAzureImport)
            {
                // Refuse to repoint an existing Azure link at a different VNet. Two VNets in one
                // subscription may share a prefix, so a subnet matching this VNet's address may
                // still have been imported from another one. Overwriting the link here is invisible
                // - the row keeps its old name - and makes reconcile measure it against a VNet it
                // was never imported from, archiving it and its subtree when that VNet is removed.
                // ARM IDs are path-based, so re-importing the same VNet, including after a
                // delete-and-recreate under the same name, compares equal and still works.
                if (!string.IsNullOrEmpty(vnetResourceId)
                    && !string.IsNullOrEmpty(parentSubnet.AzureResourceId)
                    && !string.Equals(parentSubnet.AzureResourceId, vnetResourceId, StringComparison.OrdinalIgnoreCase))
                {
                    string conflict =
                        $"Bastet subnet '{parentSubnet.Name}' ({parentSubnet.NetworkAddress}/{parentSubnet.Cidr}) is already "
                        + $"linked to Azure VNet '{parentSubnet.AzureResourceId}' and cannot be re-linked to '{vnetResourceId}' "
                        + "by an import. If the VNet was renamed or moved, delete the Bastet subnet and import it again.";

                    ModelState.AddModelError("subnets", conflict);
                    await transaction.RollbackAsync();
                    return BatchCreateFailure(isAzureImport, parentId, conflict, Conflict(new { success = false, error = conflict }));
                }

                // Update the name to match the Azure VNet name. Azure VNet names reach 64 characters
                // and the column holds 100, so this never truncates a real Azure name - it is a guard
                // against a hand-crafted post, since SanitizeName trims at the same 100.
                parentSubnet.Name = vnetName.Length > MaxSubnetNameLength
                    ? vnetName[..MaxSubnetNameLength]
                    : vnetName;

                // Stamp the VNet resource ID onto the parent so the Details page can link to Azure.
                if (!string.IsNullOrEmpty(vnetResourceId))
                {
                    parentSubnet.AzureResourceId = vnetResourceId;
                }

                // If a subnet fully encompasses the VNet address prefix, mark parent as fully allocated
                if (hasFullyEncompassingSubnet)
                {
                    parentSubnet.IsFullyAllocated = true;

                    // Update description, preserving existing description if present
                    parentSubnet.Description = AppendFullyAllocatedNote(parentSubnet.Description, fullyEncompassingSubnetName);
                }

                parentSubnet.LastModifiedAt = DateTime.UtcNow;
                parentSubnet.ModifiedBy = userContextService.GetCurrentUsername();
                await context.SaveChangesAsync();
            }

            // If we have a subnet that fully encompasses the VNet address prefix,
            // we don't create any child subnets
            if (!hasFullyEncompassingSubnet)
            {
                // Settled before the loop so every row is named against the whole batch.
                Dictionary<int, string> importNames = ResolveImportNames(subnets);

                // Create each subnet - with validation right before adding to catch overlaps
                for (int i = 0; i < subnets.Count; i++)
                {
                    AzureImportSubnetViewModel subnet = subnets[i];

                    // Skip subnets that fully encompass the VNet address prefix
                    if (subnet.FullyEncompassesVNetPrefix)
                    {
                        continue;
                    }

                    // Validate before adding, against the batch's tree snapshot plus everything this
                    // batch has already created, so conflicts with earlier entries are caught too.
                    if (!await ValidateSubnetCreation(subnet, treeCache))
                    {
                        // Validation failed, rollback and return errors
                        await transaction.RollbackAsync();
                        return BatchCreateFailure(isAzureImport, parentId,
                            ModelStateMessage("The import could not be applied."), BadRequest(ModelState));
                    }

                    // Create the subnet entity
                    Subnet newSubnet = new()
                    {
                        Name = importNames[i],
                        NetworkAddress = subnet.NetworkAddress,
                        Cidr = subnet.Cidr,
                        Description = subnet.Description,
                        Tags = subnet.Tags,
                        AzureResourceId = subnet.AzureResourceId,
                        ParentSubnetId = parentId,
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = userContextService.GetCurrentUsername()
                    };

                    context.Subnets.Add(newSubnet);
                    await context.SaveChangesAsync();

                    // Keep the snapshot current, or the next entry in this batch could be created
                    // overlapping this one.
                    treeCache.Add(newSubnet);

                    createdSubnetIds.Add(newSubnet.Id);
                }
            }

            await transaction.CommitAsync();

            // Add appropriate success message
            TempData["SuccessMessage"] = hasFullyEncompassingSubnet
                ? $"Successfully renamed parent subnet to '{vnetName}' and marked it as fully allocated by Azure subnet '{fullyEncompassingSubnetName}'."
                : !string.IsNullOrEmpty(vnetName) && isAzureImport
                    ? $"Successfully renamed parent subnet to '{vnetName}' and imported {createdSubnetIds.Count} child subnets."
                    : (object)$"Successfully imported {createdSubnetIds.Count} subnets.";

            // If this was called from the Azure import flow, redirect to details
            if (isAzureImport)
            {
                return RedirectToAction("Details", new { id = parentId });
            }

            // Otherwise return JSON (for API usage)
            return Ok(new { success = true, subnetIds = createdSubnetIds });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Batch create of child subnets under parent {ParentId} failed", parentId);
            await TransactionCleanup.RollbackQuietlyAsync(transaction, logger);
            const string unexpected = "An unexpected error occurred while creating subnets. Details have been logged.";
            return BatchCreateFailure(isAzureImport, parentId, unexpected, StatusCode(500, unexpected));
        }
    }
}
