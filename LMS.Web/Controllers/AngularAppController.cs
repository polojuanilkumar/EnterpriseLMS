using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace LMS.Web.Controllers;
// Optional /app-only fallback. MVC, /bff and token-entry routes are never SPA fallbacks.
[AllowAnonymous]
public sealed class AngularAppController(IConfiguration configuration,IWebHostEnvironment environment):ControllerBase
{
    [HttpGet("app"),HttpGet("app/{**path}")]
    public IActionResult Index(string? path)
    {
        if(!configuration.GetValue<bool>("Angular:Enabled")||path?.Split('/').Any(p=>p.Contains('.'))==true)return NotFound();
        var file=Path.Combine(environment.WebRootPath??Path.Combine(environment.ContentRootPath,"wwwroot"),"app","index.html");
        if(!System.IO.File.Exists(file))return NotFound();
        Response.Headers.CacheControl="no-store";
        return PhysicalFile(file,"text/html; charset=utf-8");
    }
}
