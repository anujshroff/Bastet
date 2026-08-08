using Bastet.Services;
using Bastet.Services.Locking;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Moq;

namespace Bastet.Tests.TestHelpers;

public static class ControllerTestHelper
{

    public static T SetupController<T>(T controller) where T : Controller
    {

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        TempDataDictionary tempData = new(
            controller.ControllerContext.HttpContext,
            Mock.Of<ITempDataProvider>());

        controller.TempData = tempData;

        return controller;
    }

    public static IUserContextService CreateMockUserContextService(string username = "test-user")
    {
        Mock<IUserContextService> mock = new();
        mock.Setup(m => m.GetCurrentUsername()).Returns(username);
        return mock.Object;
    }

    public static ISubnetLockingService CreateMockSubnetLockingService() => new NoOpSubnetLockingService();
}

public class NoOpSubnetLockingService : ISubnetLockingService
{
    public async Task<T> ExecuteWithSubnetLockAsync<T>(Func<Task<T>> operation, TimeSpan? timeout = null) =>

        await operation();
}
