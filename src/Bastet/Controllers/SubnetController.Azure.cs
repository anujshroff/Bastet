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

}
