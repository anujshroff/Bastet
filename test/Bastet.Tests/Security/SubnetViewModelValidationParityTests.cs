using Bastet.Models.ViewModels;
using Bastet.Services.Security;
using System.ComponentModel.DataAnnotations;

namespace Bastet.Tests.Security;

public class SubnetViewModelValidationParityTests
{

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
