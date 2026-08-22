using Bastet.Models.ViewModels;
using Bastet.Services.Security;
using System.ComponentModel.DataAnnotations;

namespace Bastet.Tests.Security;

public class HostIpViewModelValidationParityTests
{
    private static readonly IInputSanitizationService _service = new InputSanitizationService();

    private sealed class SanitizationServiceProvider : IServiceProvider
    {
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

    private static void AssertValid(object model)
    {
        List<ValidationResult> results = Validate(model);
        Assert.True(results.Count == 0,
            $"{model.GetType().Name} was rejected: "
            + string.Join(" | ", results.Select(r => r.ErrorMessage)));
    }

    private static void AssertRejects(object model, string expectedMessageFragment)
    {
        List<ValidationResult> results = Validate(model);
        Assert.Contains(results, r => r.ErrorMessage is not null && r.ErrorMessage.Contains(expectedMessageFragment));
    }

    [Theory]
    [InlineData("core/edge (site B)")]
    [InlineData("Prod: DC1")]
    [InlineData("Zürich core")]
    [InlineData("Bob's Lab")]
    [InlineData("HQ <-> DR")]
    public void OrdinaryOperatorHostNames_AcceptedByBoth(string name)
    {
        AssertValid(new CreateHostIpViewModel { IP = "10.0.0.5", SubnetId = 1, Name = name });
        AssertValid(new EditHostIpViewModel { IP = "10.0.0.5", SubnetId = 1, Name = name });
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("Site <HQ>")]
    public void MarkupInHostNames_RejectedByBoth(string name)
    {
        AssertRejects(new CreateHostIpViewModel { IP = "10.0.0.5", SubnetId = 1, Name = name },
            "HTML tags are not allowed");
        AssertRejects(new EditHostIpViewModel { IP = "10.0.0.5", SubnetId = 1, Name = name },
            "HTML tags are not allowed");
    }

    [Theory]
    [InlineData("not-an-ip")]
    [InlineData("10.0.0.999")]
    public void MalformedIp_RejectedAtTheAttributeLayer(string ip) =>
        AssertRejects(new CreateHostIpViewModel { IP = ip, SubnetId = 1, Name = "ok" },
            "Invalid IP address format");

    [Fact]
    public void DottedQuadIp_AcceptedAtTheAttributeLayer() =>
        AssertValid(new CreateHostIpViewModel { IP = "192.168.10.5", SubnetId = 1, Name = "ok" });
}
