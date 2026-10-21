using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;

namespace LMS.Web.Services;

// No token is placed in an authentication ticket, view model, URL, or browser storage.
public sealed class LearnerSession
{
    public string AccessToken { get; init; } = "";
    public Guid UserId { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public Guid? OrganizationId { get; init; }
    public string? OrganizationRole { get; init; }
    public bool IsSuperAdmin { get; init; }
}
public sealed class LearnerSessions(IMemoryCache cache, IDataProtectionProvider protection, TimeProvider clock)
{
    public const string Claim = "lms-session";
    private readonly IDataProtector protector = protection.CreateProtector("LMS.Web.ServerSession.v1");
    public string Create(LearnerSession session)
    {
        if (session.UserId == Guid.Empty || string.IsNullOrWhiteSpace(session.AccessToken) || session.ExpiresAt <= clock.GetUtcNow())
            throw new WebApiException(401);
        var id = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        cache.Set("learner:"+id,protector.Protect(JsonSerializer.Serialize(session)),session.ExpiresAt);
        return id;
    }
    public LearnerSession? Find(string? id)
    {
        if (id is null || !cache.TryGetValue<string>("learner:"+id,out var encrypted) || encrypted is null) return null;
        try
        {
            var session=JsonSerializer.Deserialize<LearnerSession>(protector.Unprotect(encrypted));
            if(session is not null && session.ExpiresAt > clock.GetUtcNow())return session;
        }
        catch (CryptographicException) { }
        catch (JsonException) { }
        Remove(id);return null;
    }
    public LearnerSession Require(ClaimsPrincipal user, Guid? organizationId = null)
    {
        if(user.Identity?.IsAuthenticated!=true)throw new WebApiException(401);
        var session=Find(user.FindFirstValue(Claim)) ?? throw new WebApiException(401);
        if(organizationId.HasValue && session.OrganizationId != organizationId)throw new WebApiException(403);
        return session;
    }
    public LearnerSession RequireLearner(ClaimsPrincipal user, Guid organizationId)
    {
        var session = Require(user, organizationId);
        if (session.OrganizationRole != "Student" || session.IsSuperAdmin) throw new WebApiException(403);
        return session;
    }
    public void Remove(string? id) { if(id is not null)cache.Remove("learner:"+id); }
}
