using System.Text.Json;
using LMS.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace LMS.Tests;

public sealed class GlobalExceptionMiddlewareTests
{
    public static TheoryData<Exception, int> KnownErrors => new()
    {
        { new UnauthorizedAccessException("Not the enrollment owner."), 403 },
        { new ArgumentException("Invalid request."), 400 },
        { new ArgumentOutOfRangeException("progress", "Out of range."), 400 },
        { new KeyNotFoundException("Enrollment not found."), 404 },
        { new InvalidOperationException("Only active enrollments can be cancelled."), 409 }
    };

    [Theory]
    [MemberData(nameof(KnownErrors))]
    public async Task Known_errors_use_existing_envelope_and_public_message(Exception error, int status)
    {
        var (context, logger) = await Invoke(error);
        Assert.Equal(status, context.Response.StatusCode);
        Assert.Equal("application/json", context.Response.ContentType);
        using var body = JsonDocument.Parse(ReadBody(context));
        var root = body.RootElement;
        Assert.Equal(new[] { "data", "errors", "message", "success" },
            root.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray());
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(error.Message, root.GetProperty("message").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("data").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("errors").ValueKind);
        VerifyLogged(logger, error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unexpected_errors_hide_details_but_log_the_original_exception(bool nested)
    {
        // Synthetic markers, never real credentials or connection strings.
        Exception error = nested
            ? new Exception("private-outer-marker", new IOException("private-inner-marker"))
            : new IOException("private-outer-marker");
        var (context, logger) = await Invoke(error);
        Assert.Equal(500, context.Response.StatusCode);
        Assert.Equal("application/json", context.Response.ContentType);
        var json = ReadBody(context);
        using var body = JsonDocument.Parse(json);
        Assert.Equal(4, body.RootElement.EnumerateObject().Count());
        Assert.False(body.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("An unexpected error occurred.", body.RootElement.GetProperty("message").GetString());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("data").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("errors").ValueKind);
        Assert.DoesNotContain("private-", json);
        Assert.DoesNotContain("IOException", json);
        Assert.DoesNotContain("stack", json, StringComparison.OrdinalIgnoreCase);
        VerifyLogged(logger, error);
    }

    [Fact]
    public async Task Uses_configured_MVC_serializer_options()
    {
        var options = new JsonOptions();
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseUpper;
        var (context, _) = await Invoke(new UnauthorizedAccessException("Denied."), options);
        using var body = JsonDocument.Parse(ReadBody(context));
        Assert.False(body.RootElement.GetProperty("SUCCESS").GetBoolean());
        Assert.False(body.RootElement.TryGetProperty("success", out _));
    }

    [Fact]
    public async Task Successful_downstream_response_is_unchanged_and_not_logged()
    {
        var logger = new Mock<ILogger<GlobalExceptionMiddleware>>();
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var calls = 0;
        var middleware = new GlobalExceptionMiddleware(async ctx =>
        {
            calls++;
            ctx.Response.StatusCode = 201;
            ctx.Response.ContentType = "text/plain";
            await ctx.Response.WriteAsync("created");
        }, logger.Object, Options.Create(new JsonOptions()));
        await middleware.InvokeAsync(context);
        Assert.Equal(1, calls);
        Assert.Equal(201, context.Response.StatusCode);
        Assert.Equal("text/plain", context.Response.ContentType);
        Assert.Equal("created", ReadBody(context));
        logger.VerifyNoOtherCalls();
    }

    private static async Task<(DefaultHttpContext, Mock<ILogger<GlobalExceptionMiddleware>>)> Invoke(
        Exception error, JsonOptions? options = null)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var logger = new Mock<ILogger<GlobalExceptionMiddleware>>();
        var middleware = new GlobalExceptionMiddleware(_ => Task.FromException(error),
            logger.Object, Options.Create(options ?? new JsonOptions()));
        await middleware.InvokeAsync(context);
        return (context, logger);
    }

    private static string ReadBody(HttpContext context)
        => System.Text.Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());

    private static void VerifyLogged(Mock<ILogger<GlobalExceptionMiddleware>> logger, Exception error)
    {
        logger.Verify(x => x.Log(LogLevel.Error, It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((value, _) => value.ToString() == "Unhandled exception occurred."),
            It.Is<Exception>(actual => ReferenceEquals(actual, error)),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }
}
