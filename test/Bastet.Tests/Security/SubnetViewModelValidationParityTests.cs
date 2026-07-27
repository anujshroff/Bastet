using Bastet.Models.ViewModels;
using Bastet.Services.Security;
using System.ComponentModel.DataAnnotations;

namespace Bastet.Tests.Security;

/// <summary>
/// Create and Edit write the same three columns, so they must refuse the same input.
///
/// Where they disagree the damage is silent rather than loud: GlobalSanitizationFilter runs after
/// model binding and validation, so a value Edit's model accepts is rewritten by the sanitizer
/// afterwards and stored in its mangled form with a success message. StripHtml can empty a value
/// outright, which defeats [Required]; SanitizeTags drops over-long tags and everything past the
/// tenth. Create rejects all of it up front because it carries the validators.
/// </summary>
public class SubnetViewModelValidationParityTests
{
    /// <summary>
    /// [NoHtml], [SafeText] and [Tags] all resolve IInputSanitizationService from the validation
    /// context. Without one they fail with "Input sanitization service not available", which would
    /// make every assertion below pass for entirely the wrong reason.
    /// </summary>
    private sealed class SanitizationServiceProvider : IServiceProvider
    {
        private readonly InputSanitizationService _service = new();

        public object? GetService(Type serviceType) =>
            serviceType == typeof(IInputSanitizationService) ? _service : null;
    }

    private static List<ValidationResult> Validate(object model)
    {
        List<ValidationResult> results = [];
        _ = Validator.TryValidateObject(
            model,
            new ValidationContext(model, new SanitizationServiceProvider(), null),
            results,
            validateAllProperties: true);
        return results;
    }

    /// <summary>
    /// Validates one property in isolation. [NoHtml] and [Tags] both return a ValidationResult with
    /// no member names, so a whole-object validation cannot say which property failed - matching on
    /// MemberNames silently never hits. Setting MemberName on the context makes the attribution
    /// exact, and the value passed is read from the property by reflection so the two cannot
    /// disagree.
    /// </summary>
    private static void AssertRejects(object model, string member, string expectedMessageFragment)
    {
        object? value = model.GetType().GetProperty(member)!.GetValue(model);

        List<ValidationResult> results = [];
        bool ok = Validator.TryValidateProperty(
            value,
            new ValidationContext(model, new SanitizationServiceProvider(), null) { MemberName = member },
            results);

        Assert.False(ok, $"{model.GetType().Name}.{member} was accepted but should have been rejected.");
        Assert.Contains(results, r => r.ErrorMessage is not null && r.ErrorMessage.Contains(expectedMessageFragment));
    }

    // Name -------------------------------------------------------------------

    /// <summary>
    /// The worst case: StripHtml reduces this to the empty string, so [Required] passes on the
    /// submitted value and the row is stored with no name at all.
    /// </summary>
    [Theory]
    [InlineData("<b></b>")]
    [InlineData("Site <HQ>")]
    public void MarkupInName_RejectedByBoth(string name)
    {
        AssertRejects(
            new CreateSubnetViewModel { Name = name, NetworkAddress = "10.0.0.0", Cidr = 24 },
            nameof(CreateSubnetViewModel.Name),
            "HTML tags are not allowed");

        AssertRejects(
            new EditSubnetViewModel { Id = 1, Name = name, NetworkAddress = "10.0.0.0", Cidr = 24 },
            nameof(EditSubnetViewModel.Name),
            "HTML tags are not allowed");
    }

    // Description ------------------------------------------------------------

    /// <summary>
    /// StripHtml's `&lt;[^&gt;]*&gt;` eats everything between the two comparison operators, so this
    /// stores as "temp  3".
    /// </summary>
    [Fact]
    public void MarkupInDescription_RejectedByBoth()
    {
        const string description = "temp < 5 and load > 3";

        AssertRejects(
            new CreateSubnetViewModel { Name = "ok", NetworkAddress = "10.0.0.0", Cidr = 24, Description = description },
            nameof(CreateSubnetViewModel.Description),
            "HTML tags are not allowed");

        AssertRejects(
            new EditSubnetViewModel { Id = 1, Name = "ok", NetworkAddress = "10.0.0.0", Cidr = 24, Description = description },
            nameof(EditSubnetViewModel.Description),
            "HTML tags are not allowed");
    }

    // Tags -------------------------------------------------------------------

    /// <summary>A single over-long tag is dropped outright by SanitizeTags' length filter.</summary>
    [Fact]
    public void OverlongTag_RejectedByBoth()
    {
        string tags = new('a', 60);

        AssertRejects(
            new CreateSubnetViewModel { Name = "ok", NetworkAddress = "10.0.0.0", Cidr = 24, Tags = tags },
            nameof(CreateSubnetViewModel.Tags),
            "50 characters or less");

        AssertRejects(
            new EditSubnetViewModel { Id = 1, Name = "ok", NetworkAddress = "10.0.0.0", Cidr = 24, Tags = tags },
            nameof(EditSubnetViewModel.Tags),
            "50 characters or less");
    }

    /// <summary>Everything past the tenth tag is discarded by SanitizeTags' Take(10).</summary>
    [Fact]
    public void TooManyTags_RejectedByBoth()
    {
        string tags = string.Join(",", Enumerable.Range(1, 11).Select(i => $"t{i}"));

        AssertRejects(
            new CreateSubnetViewModel { Name = "ok", NetworkAddress = "10.0.0.0", Cidr = 24, Tags = tags },
            nameof(CreateSubnetViewModel.Tags),
            "Maximum 10 tags allowed");

        AssertRejects(
            new EditSubnetViewModel { Id = 1, Name = "ok", NetworkAddress = "10.0.0.0", Cidr = 24, Tags = tags },
            nameof(EditSubnetViewModel.Tags),
            "Maximum 10 tags allowed");
    }

    // Parity in the other direction ------------------------------------------

    /// <summary>
    /// The guard against over-correcting: ordinary values both models are supposed to accept must
    /// still pass. One tag sits exactly on the 50-character limit and there are exactly ten of them,
    /// so both [Tags] boundaries are touched while staying inside the 255-character [StringLength]
    /// that governs the field as a whole.
    /// </summary>
    [Fact]
    public void OrdinaryValues_AcceptedByBoth()
    {
        string tags = string.Join(",", [new string('a', 50), .. Enumerable.Range(1, 9).Select(i => $"tag-{i}")]);
        const string description = "Primary transit range (site A), reviewed 2026-01-01.";

        Assert.Empty(Validate(new CreateSubnetViewModel
        {
            Name = "HQ core",
            NetworkAddress = "10.0.0.0",
            Cidr = 24,
            Description = description,
            Tags = tags
        }));

        Assert.Empty(Validate(new EditSubnetViewModel
        {
            Id = 1,
            Name = "HQ core",
            NetworkAddress = "10.0.0.0",
            Cidr = 24,
            Description = description,
            Tags = tags
        }));
    }
}
