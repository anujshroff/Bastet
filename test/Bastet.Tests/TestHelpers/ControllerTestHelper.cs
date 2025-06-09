using Bastet.Services;
using Bastet.Services.Locking;
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
    /// <returns>The configured controller</returns>
    public static T SetupController<T>(T controller) where T : Controller
    {
        // Set up HTTP context
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        // Set up TempData (needed for success/error messages)
        TempDataDictionary tempData = new(
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
        Mock<IUserContextService> mock = new();
        mock.Setup(m => m.GetCurrentUsername()).Returns(username);
        return mock.Object;
    }

    /// <summary>
    /// Creates a no-op subnet locking service that simply executes operations without locking
    /// </summary>
    /// <returns>A no-op subnet locking service</returns>
    public static ISubnetLockingService CreateMockSubnetLockingService() => new NoOpSubnetLockingService();
}

/// <summary>
/// A no-op implementation of ISubnetLockingService for testing
/// Simply executes operations without any locking
/// </summary>
public class NoOpSubnetLockingService : ISubnetLockingService
{
    public async Task<T> ExecuteWithSubnetLockAsync<T>(Func<Task<T>> operation, TimeSpan? timeout = null) =>
        // Simply execute the operation without any locking
        await operation();

    public async Task<T> ExecuteWithSubnetEditLockAsync<T>(int subnetId, Func<Task<T>> operation, TimeSpan? timeout = null) =>
        // Simply execute the operation without any locking
        await operation();
}
