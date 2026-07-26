using Bastet.Filters;
using Bastet.Models.ViewModels;
using Bastet.Services.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Bastet.Tests.Security;

/// <summary>
/// The filter logs the value as it arrived, which is the point - an operator wants to see what
/// changed - so that value must not be able to write extra lines into the log.
/// </summary>
public class GlobalSanitizationFilterLoggingTests
{
    /// <summary>Captures formatted log messages so the rendered text can be asserted on.</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<string> Messages { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Messages);

        public void Dispose() { }

        private sealed class CapturingLogger(List<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) => messages.Add(formatter(state, exception));
        }
    }

    [Fact]
    public async Task SanitizedValueWithLineBreaks_IsNotLoggedWithThem()
    {
        CapturingLoggerProvider provider = new();
        using ILoggerFactory factory = LoggerFactory.Create(b =>
        {
            b.AddProvider(provider);
            b.SetMinimumLevel(LogLevel.Debug);
        });

        GlobalSanitizationFilter filter = new(
            new InputSanitizationService(),
            factory.CreateLogger<GlobalSanitizationFilter>());

        // The trailing space is what makes sanitization change the value and so triggers the log line;
        // the newline is the part that would forge a second entry.
        CreateSubnetViewModel viewModel = new()
        {
            Name = "subnet-a\nwarn: Bastet: admin login from 1.2.3.4 ",
            NetworkAddress = "10.0.0.0",
            Cidr = 24
        };

        ActionExecutingContext context = new(
            new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()),
            [],
            new Dictionary<string, object?> { ["viewModel"] = viewModel },
            controller: null!);

        await filter.OnActionExecutionAsync(context, () =>
            Task.FromResult<ActionExecutedContext>(new(context, [], controller: null!)));

        string logged = Assert.Single(provider.Messages, m => m.Contains("Sanitized property"));
        Assert.DoesNotContain("\n", logged);
        Assert.DoesNotContain("\r", logged);
        Assert.Contains("subnet-a", logged);
    }
}
