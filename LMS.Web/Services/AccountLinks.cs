using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.WebUtilities;
using System.Security.Cryptography;
using System.Text.Json;
namespace LMS.Web.Services;
public sealed record PendingAccountLink(string Token,string Email);
public sealed class AccountLinks(IMemoryCache cache,IDataProtectionProvider protection)
{
    private readonly IDataProtector protector=protection.CreateProtector("LMS.AccountLinks.v1");
    public void Store(HttpContext http,string purpose,string token,string email)
    {
        if(token.Length>4096 || email.Length>256)throw new WebApiException(400);
        var id=WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        cache.Set("account-link:"+purpose+":"+id,protector.Protect(JsonSerializer.Serialize(new PendingAccountLink(token,email))),TimeSpan.FromMinutes(20));
        http.Response.Cookies.Append("__Host-LmsLink-"+purpose,protector.Protect(id),new CookieOptions
            {HttpOnly=true,Secure=true,SameSite=SameSiteMode.Lax,Path="/",MaxAge=TimeSpan.FromMinutes(20)});
    }
    public PendingAccountLink Require(HttpContext http,string purpose)
    {
        try
        {
            var cookie=http.Request.Cookies["__Host-LmsLink-"+purpose];
            if(cookie is not null && cache.TryGetValue<string>("account-link:"+purpose+":"+protector.Unprotect(cookie),out var encrypted) && encrypted is not null)
                return JsonSerializer.Deserialize<PendingAccountLink>(protector.Unprotect(encrypted)) ?? throw new WebApiException(400);
        }catch(CryptographicException){}
        throw new WebApiException(400);
    }
    public void Remove(HttpContext http,string purpose)
    {
        try {var cookie=http.Request.Cookies["__Host-LmsLink-"+purpose];if(cookie is not null)cache.Remove("account-link:"+purpose+":"+protector.Unprotect(cookie));}catch(CryptographicException){}
        http.Response.Cookies.Delete("__Host-LmsLink-"+purpose,new CookieOptions {Secure=true,Path="/"});
    }
}
