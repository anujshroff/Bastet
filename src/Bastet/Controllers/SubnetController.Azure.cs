using Bastet.Models.ViewModels;
using Bastet.Models;
using Bastet.Services.Data;
using Bastet.Services.Security;
using Bastet.Services.Validation;
using Bastet.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bastet.Controllers;

public partial class SubnetController : Controller
{

    private const int MaxSubnetNameLength = 100;

    private const int MaxSubnetDescriptionLength = 1000;

    private const int MaxAzureResourceIdLength = 500;

    private IActionResult BatchCreateFailure(bool isAzureImport, int parentId, string message, IActionResult apiResult)
    {
        if (!isAzureImport)
        {
            return apiResult;
        }

        TempData["ErrorMessage"] = message;
        return RedirectToAction("Details", new { id = parentId });
    }

    private string ModelStateMessage(string fallback) =>
        ModelState.Values
            .SelectMany(v => v.Errors)
            .Select(e => e.ErrorMessage)
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .ToList() is { Count: > 0 } messages
                ? string.Join(" ", messages)
                : fallback;

    private static bool IsAzureResourceIdTooLong(string? resourceId) =>
        resourceId?.Length > MaxAzureResourceIdLength;

    private static string AppendFullyAllocatedNote(string? existingDescription, string? azureSubnetName) =>
        FullyAllocatedNote.Append(existingDescription, azureSubnetName, MaxSubnetDescriptionLength);

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = "RequireAdminRole")]
    public async Task<IActionResult> BatchCreateChildSubnets(int parentId, List<AzureImportSubnetViewModel> subnets, string? vnetName = null, string? vnetResourceId = null, bool isAzureImport = false, [FromServices] IInputSanitizationService? sanitizationService = null)
    {

        if (!ModelState.IsValid)
        {
            return BatchCreateFailure(isAzureImport, parentId,
                ModelStateMessage("The submitted subnets were not valid."), BadRequest(ModelState));
        }

        if (subnets is null or { Count: 0 })
        {
            ModelState.AddModelError("subnets", "No subnets were submitted for import.");
            return BatchCreateFailure(isAzureImport, parentId,
                "No subnets were submitted for import.", BadRequest(ModelState));
        }

        bool writesAzureState = isAzureImport
            || !string.IsNullOrEmpty(vnetResourceId)
            || subnets.Exists(s => !string.IsNullOrEmpty(s.AzureResourceId));

        if (writesAzureState && !AzureController.IsAzureImportEnabled())
        {
            return BatchCreateFailure(isAzureImport, parentId,
                "Azure Import feature is not enabled.",
                StatusCode(403, new { success = false, error = "Azure Import feature is not enabled" }));
        }

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

            if (!string.IsNullOrEmpty(vnetName))
            {
                vnetName = sanitizationService.SanitizeName(vnetName);
            }

            if (!string.IsNullOrEmpty(vnetResourceId))
            {
                vnetResourceId = sanitizationService.SanitizeDescription(vnetResourceId);
            }
        }

        if (subnets.Exists(s => IsAzureResourceIdTooLong(s.AzureResourceId)) || IsAzureResourceIdTooLong(vnetResourceId))
        {
            string tooLong = $"An Azure resource ID is longer than {MaxAzureResourceIdLength} characters and cannot be stored.";
            ModelState.AddModelError("subnets", tooLong);
            return BatchCreateFailure(isAzureImport, parentId, tooLong, BadRequest(ModelState));
        }

        if (subnets.Exists(s => s.FullyEncompassesVNetPrefix) && (!isAzureImport || string.IsNullOrEmpty(vnetName)))
        {
            ModelState.AddModelError("subnets",
                "A subnet marked as fully encompassing the VNet prefix can only be submitted as part of an Azure "
                + "import, which requires isAzureImport to be set and a vnetName to be supplied. It marks the parent "
                + "fully allocated rather than being created as a child, so on its own it would import nothing.");
            return BatchCreateFailure(isAzureImport, parentId,
                ModelStateMessage("The import could not be applied."), BadRequest(ModelState));
        }

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

            return await subnetLockingService.ExecuteWithSubnetLockAsync(() =>
                BatchCreateChildSubnetsCore(parentId, subnets, vnetName, vnetResourceId, isAzureImport));
        }
        catch (TimeoutException)
        {
            const string busy = "The operation timed out because another subnet operation is in progress. Please try again.";
            return BatchCreateFailure(isAzureImport, parentId, busy, StatusCode(503, busy));
        }
    }

    private static bool HasPersistedSiblingHoldingSameVNet(
        Models.Subnet target, string? vnetResourceId, List<Models.Subnet> persistedSubnets) =>
        !string.IsNullOrEmpty(vnetResourceId)
        && persistedSubnets.Any(e =>
            e.Id != target.Id
            && !string.IsNullOrEmpty(e.AzureResourceId)
            && string.Equals(e.AzureResourceId, vnetResourceId, StringComparison.OrdinalIgnoreCase));

    private static bool HasPersistedSiblingFromSameAzureSubnet(
        AzureImportSubnetViewModel subnet,
        IReadOnlyList<Subnet> persistedSubnets) =>
        persistedSubnets.Any(e =>
            !string.IsNullOrEmpty(e.AzureResourceId)
            && string.Equals(e.AzureResourceId, subnet.AzureResourceId, StringComparison.OrdinalIgnoreCase)
            && !(e.Cidr == subnet.Cidr
                 && string.Equals(e.NetworkAddress, subnet.NetworkAddress, StringComparison.OrdinalIgnoreCase)));

    public static Dictionary<int, string> ResolveImportNames(
        List<AzureImportSubnetViewModel> subnets,
        IReadOnlyList<Subnet> persistedSubnets)
    {

        HashSet<string> multiPrefixResourceIds = new(
            subnets
            .Where(s => !s.FullyEncompassesVNetPrefix && !string.IsNullOrEmpty(s.AzureResourceId))
            .GroupBy(s => s.AzureResourceId!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key),
            StringComparer.OrdinalIgnoreCase);

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
                && (multiPrefixResourceIds.Contains(subnet.AzureResourceId)
                    || HasPersistedSiblingFromSameAzureSubnet(subnet, persistedSubnets));

            if (sharesAnAzureSubnet || used.Contains(name))
            {

                name = SubnetNaming.WithSuffix(
                    name, $" ({subnet.NetworkAddress}-{subnet.Cidr})", MaxSubnetNameLength);
            }

            used.Add(name);
            names[i] = name;
        }

        return names;
    }

    private async Task<IActionResult> BatchCreateChildSubnetsCore(int parentId, List<AzureImportSubnetViewModel> subnets, string? vnetName, string? vnetResourceId, bool isAzureImport)
    {

        using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();

        try
        {

            Subnet? parentSubnet = await context.Subnets.FindAsync(parentId);
            if (parentSubnet == null)
            {
                await transaction.RollbackAsync();
                string missing = $"Parent subnet with ID {parentId} not found";
                if (isAzureImport)
                {

                    TempData["ErrorMessage"] = missing;
                    return RedirectToAction("Index");
                }

                return NotFound(missing);
            }

            List<int> createdSubnetIds = [];
            AzureImportSubnetViewModel? fullyEncompassingSubnet = null;

            bool parentRenamed = false;

            List<Subnet> treeCache = await LoadSubnetTreeForBatchAsync();

            foreach (AzureImportSubnetViewModel subnet in subnets)
            {

                subnet.ParentSubnetId = parentId;

                if (subnet.FullyEncompassesVNetPrefix)
                {
                    fullyEncompassingSubnet = subnet;
                }
            }

            bool hasFullyEncompassingSubnet = fullyEncompassingSubnet != null;
            string? fullyEncompassingSubnetName = fullyEncompassingSubnet?.Name;

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

            if (!string.IsNullOrEmpty(vnetName) && isAzureImport)
            {

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

                bool targetIsPopulated = treeCache.Exists(s => s.ParentSubnetId == parentId);

                if (!targetIsPopulated)
                {

                    string proposed = HasPersistedSiblingHoldingSameVNet(parentSubnet, vnetResourceId, treeCache)
                        ? SubnetNaming.WithSuffix(
                            vnetName, $" ({parentSubnet.NetworkAddress}-{parentSubnet.Cidr})", MaxSubnetNameLength)
                        : vnetName.Length > MaxSubnetNameLength
                            ? vnetName[..MaxSubnetNameLength]
                            : vnetName;

                    parentRenamed = !string.Equals(parentSubnet.Name, proposed, StringComparison.Ordinal);
                    parentSubnet.Name = proposed;
                }

                if (!string.IsNullOrEmpty(vnetResourceId))
                {
                    parentSubnet.AzureResourceId = vnetResourceId;
                }

                if (hasFullyEncompassingSubnet)
                {
                    parentSubnet.IsFullyAllocated = true;

                    parentSubnet.Description = AppendFullyAllocatedNote(parentSubnet.Description, fullyEncompassingSubnetName);
                }

                parentSubnet.LastModifiedAt = DateTime.UtcNow;
                parentSubnet.ModifiedBy = userContextService.GetCurrentUsername();
                await context.SaveChangesAsync();
            }

            if (!hasFullyEncompassingSubnet)
            {

                Dictionary<int, string> importNames = ResolveImportNames(subnets, treeCache);

                for (int i = 0; i < subnets.Count; i++)
                {
                    AzureImportSubnetViewModel subnet = subnets[i];

                    if (subnet.FullyEncompassesVNetPrefix)
                    {
                        continue;
                    }

                    if (!await ValidateSubnetCreation(subnet, treeCache))
                    {

                        await transaction.RollbackAsync();
                        return BatchCreateFailure(isAzureImport, parentId,
                            ModelStateMessage("The import could not be applied."), BadRequest(ModelState));
                    }

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

                    treeCache.Add(newSubnet);

                    createdSubnetIds.Add(newSubnet.Id);
                }
            }

            await transaction.CommitAsync();

            TempData["SuccessMessage"] = hasFullyEncompassingSubnet
                ? parentRenamed
                    ? $"Successfully renamed parent subnet to '{parentSubnet.Name}' and marked it as fully allocated by Azure subnet '{fullyEncompassingSubnetName}'."
                    : $"Marked '{parentSubnet.Name}' as fully allocated by Azure subnet '{fullyEncompassingSubnetName}'."
                : !string.IsNullOrEmpty(vnetName) && isAzureImport
                    ? parentRenamed
                        ? $"Successfully renamed parent subnet to '{parentSubnet.Name}' and imported {createdSubnetIds.Count} child subnets."
                        : $"Successfully imported {createdSubnetIds.Count} child subnets."
                    : (object)$"Successfully imported {createdSubnetIds.Count} subnets.";

            if (isAzureImport)
            {
                return RedirectToAction("Details", new { id = parentId });
            }

            return Ok(new { success = true, subnetIds = createdSubnetIds });
        }
        catch (Exception ex) when (SqlSaveOutcome.IsIndeterminateTransaction(ex))
        {
            logger.LogError(ex, "Batch create of child subnets under parent {ParentId} outcome unknown", parentId);
            await TransactionCleanup.RollbackQuietlyAsync(transaction, logger);
            const string unknown = "BASTET could not confirm whether these subnets were created. Reload the subnet to see its current state before retrying.";
            return BatchCreateFailure(isAzureImport, parentId, unknown, StatusCode(500, unknown));
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
