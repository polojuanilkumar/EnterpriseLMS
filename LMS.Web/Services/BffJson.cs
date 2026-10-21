using LMS.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace LMS.Web.Services;

public static class BffJson
{
    public static bool Applies(HttpContext http) => http.Request.Path.StartsWithSegments("/bff");
    public static BffError Error(int status) => status switch
    {
        400 => new("validation_failed", "Check the submitted values."),
        401 => new("session_expired", "Your session has expired. Please sign in again."),
        403 => new("access_denied", "You do not have access to this resource."),
        404 => new("not_found", "The requested endpoint was not found."),
        405 => new("method_not_allowed", "This request method is not supported."),
        409 => new("conflict", "This change conflicts with current stored state. Refresh and review it before another explicit action."),
        413 => new("validation_failed", "The submitted request is too large."),
        415 => new("validation_failed", "Send a JSON request."),
        429 => new("rate_limited", "Too many requests. Please wait before trying again."),
        502 => new("authentication_service_unavailable", "The authentication service is unavailable. Please try again later."),
        _ => new("unexpected_error", "The request could not be completed.")
    };
    public static async Task Write(HttpContext http, int status)
    {
        http.Response.StatusCode = status;
        http.Response.Headers.Remove("Location");
        await http.Response.WriteAsJsonAsync(Error(status), http.RequestAborted);
    }
}

// Runs even when MVC authorization/antiforgery or automatic model validation short-circuits.
public sealed class BffJsonFilter : IAlwaysRunResultFilter, IOrderedFilter
{
    public int Order => int.MinValue; // Preserve antiforgery failure identity before ApiController maps client errors.
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (!BffJson.Applies(context.HttpContext)) return;
        if (context.Result is ObjectResult { Value: BffError }) return;
        var status = (context.Result as IStatusCodeActionResult)?.StatusCode;
        if (status is not >= 400) return;
        BffError error;
        if (context.Result is AntiforgeryValidationFailedResult)
            error = new("csrf_validation_failed", "Refresh the page and try again.");
        else if (status == 400 && !context.ModelState.IsValid)
        {
            // Never serialize ModelState attempted values, exception messages or supplied credentials.
            var fields = new Dictionary<string, string[]>();
            if (context.ModelState.Any(x => x.Key.Equals("Email", StringComparison.OrdinalIgnoreCase) && x.Value!.Errors.Count > 0))
                fields["email"] = ["Enter a valid email address within the allowed length."];
            if (context.ModelState.Any(x => x.Key.Equals("Password", StringComparison.OrdinalIgnoreCase) && x.Value!.Errors.Count > 0))
                fields["password"] = ["Enter a password (up to 1024 characters)."];
            error = new("validation_failed", "Check the submitted values.", fields);
        }
        else error = BffJson.Error(status.Value);
        context.Result = new ObjectResult(error) { StatusCode = status };
    }
    public void OnResultExecuted(ResultExecutedContext context) { }
}
