using LMS.Application.Common.Models;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace LMS.Api.Middleware
{
    public class GlobalExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<GlobalExceptionMiddleware> _logger;
        private readonly JsonSerializerOptions _jsonOptions;

        public GlobalExceptionMiddleware(
            RequestDelegate next,
            ILogger<GlobalExceptionMiddleware> logger,
            IOptions<JsonOptions> jsonOptions)
        {
            _next = next;
            _logger = logger;
            _jsonOptions = jsonOptions.Value.JsonSerializerOptions;
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

        private async Task HandleExceptionAsync(
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
                    context.Response.StatusCode == (int)HttpStatusCode.InternalServerError
                        ? "An unexpected error occurred."
                        : exception.Message);

            var json = JsonSerializer.Serialize(response, _jsonOptions);

            await context.Response.WriteAsync(json);
        }
    }
}
