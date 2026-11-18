using System.Text.Json;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.WebApi.Output;
using ArturRios.Util.Test.Attributes;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ArturRios.Fortuna.WebApi.Tests;

/// <summary>
///     The exception handler's answer: a request Kestrel refused keeps Kestrel's status and says what was wrong,
///     as a warning in the log; anything else is the generic 500 it always was.
/// </summary>
public sealed class ApiErrorResponsesTests
{
    [UnitTheory]
    [InlineData(StatusCodes.Status413PayloadTooLarge, RequestMessages.BodyTooLarge)]
    [InlineData(StatusCodes.Status408RequestTimeout, RequestMessages.BodyTooSlow)]
    [InlineData(StatusCodes.Status400BadRequest, RequestMessages.Unreadable)]
    public async Task GivenARequestKestrelRefused_WhenTheExceptionHandlerAnswers_ThenItKeepsKestrelsStatus(
        int status,
        string message)
    {
        var logs = new List<(LogLevel Level, string Message, Exception? Exception)>();
        var context = Context(new BadHttpRequestException("Request body too large. The max is 30000000 bytes.",
            status), logs);

        await ApiErrorResponses.WriteErrorAsync(context);
        var (contentType, body) = Written(context);

        Assert.Equal(status, context.Response.StatusCode);
        Assert.StartsWith("application/json", contentType, StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("data").ValueKind);
        Assert.Equal([message], body.GetProperty("errors").EnumerateArray().Select(error => error.GetString()));
        var entry = Assert.Single(logs);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Null(entry.Exception);
        Assert.Contains("30000000", entry.Message, StringComparison.Ordinal);
    }

    [UnitTheory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(IOException))]
    public async Task GivenAnyOtherException_WhenTheExceptionHandlerAnswers_ThenItIsTheGeneric500(Type type)
    {
        var logs = new List<(LogLevel Level, string Message, Exception? Exception)>();
        var context = Context((Exception)Activator.CreateInstance(type, "database password is hunter2")!, logs);

        await ApiErrorResponses.WriteErrorAsync(context);
        var (contentType, body) = Written(context);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.StartsWith("application/json", contentType, StringComparison.Ordinal);
        Assert.Equal([RequestMessages.UnexpectedError],
            body.GetProperty("errors").EnumerateArray().Select(error => error.GetString()));
        Assert.DoesNotContain("hunter2", body.ToString(), StringComparison.Ordinal);
        // The exception handler middleware logs it, as an unhandled exception, before calling this.
        Assert.Empty(logs);
    }

    [UnitTheory]
    [InlineData(StatusCodes.Status400BadRequest, true)]
    [InlineData(StatusCodes.Status408RequestTimeout, true)]
    [InlineData(StatusCodes.Status413PayloadTooLarge, true)]
    [InlineData(StatusCodes.Status500InternalServerError, false)]
    public void GivenABadHttpRequestException_WhenClassified_ThenOnlyAClientStatusIsTheClientsFault(
        int status,
        bool clientFault)
    {
        Assert.Equal(clientFault, ApiErrorResponses.IsClientFault(new BadHttpRequestException("refused", status)));
    }

    [UnitFact]
    public void GivenAnOrdinaryExceptionOrNone_WhenClassified_ThenItIsNotTheClientsFault()
    {
        Assert.False(ApiErrorResponses.IsClientFault(new InvalidOperationException("broken")));
        Assert.False(ApiErrorResponses.IsClientFault(null));
    }

    private static DefaultHttpContext Context(
        Exception exception,
        List<(LogLevel Level, string Message, Exception? Exception)> logs)
    {
        var services = new ServiceCollection()
            .AddLogging(logging => logging.AddProvider(new ListLoggerProvider(logs)))
            .BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Response.Body = new MemoryStream();
        context.Features.Set<IExceptionHandlerFeature>(new ExceptionHandlerFeature
        {
            Error = exception,
            Path = "/api/accounts"
        });

        return context;
    }

    private static (string ContentType, JsonElement Body) Written(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var document = JsonDocument.Parse(context.Response.Body);

        return (context.Response.ContentType!, document.RootElement.Clone());
    }

    private sealed class ListLoggerProvider(List<(LogLevel Level, string Message, Exception? Exception)> logs)
        : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new ListLogger(logs);

        public void Dispose()
        {
        }
    }

    private sealed class ListLogger(List<(LogLevel Level, string Message, Exception? Exception)> logs) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            logs.Add((logLevel, formatter(state, exception), exception));
    }
}
