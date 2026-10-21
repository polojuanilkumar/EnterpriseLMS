using LMS.Web.Services;

var builder = WebApplication.CreateBuilder(args);
// Hosting request diagnostics include query strings; account links must never enter those logs.
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.AspNetCore.Mvc", LogLevel.Warning);
builder.Services.AddLearnerWeb(builder.Configuration);

var app=builder.Build();
app.UseExceptionHandler("/Home/Error");
if(!app.Environment.IsDevelopment())app.UseHsts();
app.UseHttpsRedirection();
app.UseMiddleware<LearnerSecurity>();
// Serve only an explicitly deployed Angular build under /app; no root SPA fallback.
var angularRoot=Path.Combine(app.Environment.WebRootPath??Path.Combine(app.Environment.ContentRootPath,"wwwroot"),"app");
if(app.Configuration.GetValue<bool>("Angular:Enabled")&&Directory.Exists(angularRoot))
    app.UseStaticFiles(new StaticFileOptions {FileProvider=new Microsoft.Extensions.FileProviders.PhysicalFileProvider(angularRoot),RequestPath="/app"});
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapStaticAssets().AllowAnonymous();
app.MapControllers();
app.MapControllerRoute("default","{controller=Home}/{action=Index}/{id?}").WithStaticAssets();
app.Run();
