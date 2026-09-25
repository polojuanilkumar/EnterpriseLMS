using LMS.Application.Common.Models;
using System.Net;
using System.Text.Json;

namespace LMS.Api.Middleware
{
    public class GlobalExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<GlobalExceptionMiddleware> _logger;

        public GlobalExceptionMiddleware(
            RequestDelegate next,
            ILogger<GlobalExceptionMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Unhandled exception occurred.");

                await HandleExceptionAsync(
                    context,
                    ex);
            }
        }

        private static async Task HandleExceptionAsync(
            HttpContext context,
            Exception exception)
        {
            context.Response.ContentType =
                "application/json";

            context.Response.StatusCode =
                exception switch
                {
                    KeyNotFoundException =>
                        (int)HttpStatusCode.NotFound,

                    InvalidOperationException =>
                        (int)HttpStatusCode.Conflict,

                    ArgumentException =>
                        (int)HttpStatusCode.BadRequest,

                    _ =>
                        (int)HttpStatusCode.InternalServerError
                };

            var response =
                ApiResponse<object>.Fail(
                    exception.Message);

            var json = JsonSerializer.Serialize(response);

            await context.Response.WriteAsync(json);
        }
    }
}
