using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
namespace LMS.Web.Services;

public sealed class LearnerSecurity(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context,LearnerSessions sessions)
    {
        context.Response.Headers.CacheControl="private, no-store";
        context.Response.Headers["X-Content-Type-Options"]="nosniff";
        context.Response.Headers["Referrer-Policy"]="no-referrer";
        context.Response.Headers["Content-Security-Policy"]="default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; media-src 'self'; frame-src 'self' https://www.youtube-nocookie.com; object-src 'none'; base-uri 'none'; form-action 'self'; frame-ancestors 'self'";
        if(context.Request.Path.StartsWithSegments("/app/account"))
            context.Response.Headers["Content-Security-Policy"]="default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self'; connect-src 'self'; frame-src 'none'; object-src 'none'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'";
        try
        {
            await next(context);
            if(BffJson.Applies(context) && context.Response.StatusCode >= 400 && !context.Response.HasStarted
                && context.Response.ContentType is null)await BffJson.Write(context,context.Response.StatusCode);
        }
        catch(WebApiException error) {await Fail(context,sessions,error.Status);}
        catch(HttpRequestException) {await Fail(context,sessions,502);}
        catch(JsonException) {await Fail(context,sessions,502);}
        catch(OperationCanceledException) when(!context.RequestAborted.IsCancellationRequested) {await Fail(context,sessions,502);}
        catch(BadHttpRequestException error) when(BffJson.Applies(context)) {await Fail(context,sessions,error.StatusCode);}
        catch(Exception) when(BffJson.Applies(context)) {await Fail(context,sessions,500);}
    }
    private static async Task Fail(HttpContext http,LearnerSessions sessions,int status)
    {
        if(http.Response.HasStarted) {http.Abort();return;}
        if(status==401)
        {
            sessions.Remove(http.User.FindFirstValue(LearnerSessions.Claim));
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
        if(BffJson.Applies(http)){await BffJson.Write(http,status);return;}
        http.Response.StatusCode=status;
        if(status==403 && http.Request.Path.Value?.EndsWith("/media",StringComparison.Ordinal)!=true)
        {
            http.Response.ContentType="text/html; charset=utf-8";
            if(!HttpMethods.IsHead(http.Request.Method))await http.Response.WriteAsync(AccessDeniedPage.Html);
            return;
        }
        http.Response.ContentType="text/plain; charset=utf-8";
        if(!HttpMethods.IsHead(http.Request.Method))await http.Response.WriteAsync(status switch
        {
            400=>"The submitted form is invalid. Return to the form and check its values.",
            409=>"This change conflicts with the current resource state.",
            413=>"The uploaded file is too large.",
            429=>"Too many administration requests. Wait before trying again; no change was automatically retried.",
            401=>"Your session has expired. Please sign in again at /account/login.",
            403=>"You do not have access to this learning resource. Choose an organization at /account/organizations.",
            404=>"Learning resource not found.",
            _=>"The learning service is unavailable. Please try again later."
        });
    }
}

public static class AccessDeniedPage
{
    public const string Html = "<!doctype html><html lang=\"en\"><meta charset=\"utf-8\"><title>Access denied</title><main><h1>Access denied</h1><p>Your current organization role cannot access this page or perform this action.</p><p><a href=\"/account/organizations\">Choose another organization</a> | <a href=\"/account/login\">Sign in with another account</a></p></main></html>";
}
