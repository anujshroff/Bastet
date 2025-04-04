using Bastet.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Moq;

namespace Bastet.Tests.TestHelpers;

/// <summary>
/// Helper class for setting up controllers for testing
/// </summary>
public static class ControllerTestHelper
{
    /// <summary>
    /// Sets up a controller with HttpContext, TempData, and other common dependencies
    /// </summary>
    /// <param name="controller">The controller to set up</param>
    /// <param name="userContextService">Optional mock user context service</param>
    /// <returns>The configured controller</returns>
    public static T SetupController<T>(T controller, IUserContextService? userContextService = null) where T : Controller
    {
        // Set up HTTP context
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        // Set up TempData (needed for success/error messages)
        var tempData = new TempDataDictionary(
            controller.ControllerContext.HttpContext,
            Mock.Of<ITempDataProvider>());
        
        controller.TempData = tempData;

        return controller;
    }

    /// <summary>
    /// Creates a mock user context service
    /// </summary>
    /// <param name="username">The username to return from GetCurrentUsername</param>
    /// <returns>A mock user context service</returns>
    public static IUserContextService CreateMockUserContextService(string username = "test-user")
    {
        var mock = new Mock<IUserContextService>();
        mock.Setup(m => m.GetCurrentUsername()).Returns(username);
        return mock.Object;
    }
}
