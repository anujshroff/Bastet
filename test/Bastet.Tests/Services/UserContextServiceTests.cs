using System.Security.Claims;
using Bastet.Models;
using Bastet.Services;
using Microsoft.AspNetCore.Http;

namespace Bastet.Tests.Services;

public class UserContextServiceTests
{
    private static UserContextService ServiceFor(ClaimsPrincipal? user)
    {
        DefaultHttpContext httpContext = new();
        if (user is not null)
        {
            httpContext.User = user;
        }

        return new UserContextService(new HttpContextAccessor { HttpContext = httpContext });
    }

    private static ClaimsPrincipal SignedInWith(params string[] roles) =>
        new(new ClaimsIdentity(
            roles.Select(r => new Claim(ClaimTypes.Role, r)).Append(new Claim(ClaimTypes.Name, "alice")),
            "Test"));

    [Fact]
    public void AnAnonymousVisitor_IsNotSignedInWithoutARole()
        => Assert.False(ServiceFor(new ClaimsPrincipal(new ClaimsIdentity())).IsSignedInWithoutRole(ApplicationRoles.View));

    [Fact]
    public void ARequestWithNoHttpContext_IsNotSignedInWithoutARole()
        => Assert.False(new UserContextService(new HttpContextAccessor()).IsSignedInWithoutRole(ApplicationRoles.View));

    [Fact]
    public void ASignedInUserHoldingNoBastetRole_IsSignedInWithoutTheViewRole()
        => Assert.True(ServiceFor(SignedInWith()).IsSignedInWithoutRole(ApplicationRoles.View));

    [Theory]
    [InlineData(ApplicationRoles.View)]
    [InlineData(ApplicationRoles.Edit)]
    [InlineData(ApplicationRoles.Delete)]
    [InlineData(ApplicationRoles.Admin)]
    public void ASignedInUserHoldingAnyBastetRole_CanOpenViewPages(string role)
        => Assert.False(ServiceFor(SignedInWith(role)).IsSignedInWithoutRole(ApplicationRoles.View));

    [Fact]
    public void TheQuestionIsAskedOfTheSameRoleSetAsUserHasRole()
    {
        UserContextService viewOnly = ServiceFor(SignedInWith(ApplicationRoles.View));

        Assert.False(viewOnly.IsSignedInWithoutRole(ApplicationRoles.View));
        Assert.True(viewOnly.IsSignedInWithoutRole(ApplicationRoles.Edit));
        Assert.True(viewOnly.IsSignedInWithoutRole(ApplicationRoles.Admin));
    }
}
