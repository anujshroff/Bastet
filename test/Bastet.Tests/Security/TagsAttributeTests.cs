using System.ComponentModel.DataAnnotations;
using Bastet.Services.Security;

namespace Bastet.Tests.Security;

public class TagsAttributeTests
{
    private static ValidationResult? Validate(string value)
    {
        TagsAttribute rule = new() { MaxTags = 10, MaxTagLength = 50 };
        ValidationContext context = new(new object(), new SanitizationServiceProvider(), null);
        return rule.GetValidationResult(value, context);
    }

    [Theory]
    [InlineData("10.0.0.0/8")]
    [InlineData("owner:netops")]
    [InlineData("Zürich")]
    [InlineData("site's-gw")]
    [InlineData("HQ <-> DR")]
    [InlineData("prod, dmz, 10.0.0.0/8")]
    public void OrdinaryOperatorText_IsAccepted(string tags) =>
        Assert.Equal(ValidationResult.Success, Validate(tags));

    [Fact]
    public void MoreThanMaxTags_IsRefused()
    {
        string tags = string.Join(", ", Enumerable.Range(1, 11).Select(i => $"tag{i}"));
        Assert.NotEqual(ValidationResult.Success, Validate(tags));
    }

    [Fact]
    public void ATagLongerThanMaxTagLength_IsRefused() =>
        Assert.NotEqual(ValidationResult.Success, Validate(new string('a', 51)));

    private sealed class SanitizationServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(IInputSanitizationService) ? new InputSanitizationService() : null;
    }
}
