using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using LMS.Api.Controllers;
using LMS.Domain.Enums;
using LMS.Infrastructure.Identity;
using LMS.Web.Controllers;
using LMS.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace LMS.Tests;

public sealed class AccountOnboardingTests
{
    // All email behavior tests use this fake, never a real provider or development capture.
    private sealed class Emails : IAccountEmailSender
    {
        public readonly List<AccountEmail> Sent=[];
        public bool Fail;
        public Task SendAsync(AccountEmail email,CancellationToken ct=default)
        {if(Fail)throw new AccountDeliveryException();Sent.Add(email);return Task.CompletedTask;}
    }
    [Fact]
    public void Hash_is_fixed_length_deterministic_and_does_not_store_token()
    {
        var raw=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var hash=OrganizationInvitation.Hash(raw);
        Assert.Equal(64,hash.Length);Assert.NotEqual(raw,hash);Assert.Equal(hash,OrganizationInvitation.Hash(raw));
        Assert.NotEqual(hash,OrganizationInvitation.Hash(raw+"x"));
        Assert.DoesNotContain(typeof(OrganizationInvitation).GetProperties(),p=>p.Name=="Token");
        Assert.DoesNotContain(typeof(InvitationRow).GetProperties(),p=>p.Name.Contains("Token"));
        Assert.DoesNotContain(typeof(InvitationPage).GetProperties(),p=>p.Name.Contains("Token"));
    }
    [Fact]
    public void Expiry_boundary_and_terminal_states_deny_reuse()
    {
        var now=DateTimeOffset.UtcNow;var i=new OrganizationInvitation {CreatedAt=now,ExpiresAt=now.AddHours(48)};
        Assert.True(i.Usable(i.ExpiresAt.AddTicks(-1)));Assert.False(i.Usable(i.ExpiresAt));Assert.False(i.Usable(i.ExpiresAt.AddTicks(1)));
        i.AcceptedAt=now;Assert.False(i.Usable(now));i.AcceptedAt=null;i.RevokedAt=now;Assert.False(i.Usable(now));
        i.RevokedAt=null;i.Pending=false;Assert.False(i.Usable(now));
        var oldHash=OrganizationInvitation.Hash("old");i.TokenHash=OrganizationInvitation.Hash("replacement");Assert.NotEqual(oldHash,i.TokenHash);
    }
    [Fact]
    public void Consumption_and_revocation_are_terminal_and_reissue_preserves_history()
    {
        var now=DateTimeOffset.UtcNow;
        var accepted=new OrganizationInvitation {ExpiresAt=now.AddHours(48)};
        accepted.Consume(now);Assert.False(accepted.Pending);Assert.Equal(now,accepted.AcceptedAt);
        Assert.Throws<InvalidOperationException>(()=>accepted.Consume(now.AddSeconds(1)));
        accepted.Revoke(now.AddSeconds(2));Assert.Null(accepted.RevokedAt);
        var previous=new OrganizationInvitation {TokenHash=OrganizationInvitation.Hash("previous"),ExpiresAt=now.AddHours(48)};
        previous.Revoke(now);Assert.False(previous.Usable(now));Assert.Equal(now,previous.RevokedAt);
        Assert.Throws<InvalidOperationException>(()=>previous.Consume(now));
        var replacement=new OrganizationInvitation {TokenHash=OrganizationInvitation.Hash("replacement"),ExpiresAt=now.AddHours(48)};
        Assert.True(replacement.Usable(now));Assert.NotEqual(previous.TokenHash,replacement.TokenHash);
        var expired=new OrganizationInvitation {ExpiresAt=now};Assert.Throws<InvalidOperationException>(()=>expired.Consume(now));
    }
    [Theory]
    [InlineData(true,false,true,true)] [InlineData(true,true,false,true)]
    [InlineData(true,false,false,false)] [InlineData(false,true,true,false)]
    [InlineData(false,false,true,false)]
    public void Stored_authority_requires_active_org_and_current_admin_or_platform(bool active,bool platform,bool admin,bool allowed)
    {
        if(allowed)InvitationRules.Authority(active,platform,admin);
        else Assert.Throws<UnauthorizedAccessException>(()=>InvitationRules.Authority(active,platform,admin));
    }
    [Fact]
    public void Two_organizations_and_explicit_platform_selection_are_isolated()
    {
        var a=Guid.NewGuid();var b=Guid.NewGuid();var actor=Guid.NewGuid();
        InvitationRules.SelectedOrganization(a,a,actor);
        Assert.Throws<UnauthorizedAccessException>(()=>InvitationRules.SelectedOrganization(a,b,actor));
        Assert.Throws<UnauthorizedAccessException>(()=>InvitationRules.SelectedOrganization(null,b,actor));
        Assert.Throws<UnauthorizedAccessException>(()=>InvitationRules.SelectedOrganization(b,b,Guid.Empty));
        Assert.Equal(new[]{OrganizationRole.Student,OrganizationRole.Instructor,OrganizationRole.Admin},Enum.GetValues<OrganizationRole>());
    }
    [Fact]
    public void Existing_account_requires_verified_exact_email_identity_and_no_platform_role()
    {
        var u=new ApplicationUser {Id=Guid.NewGuid(),NormalizedEmail="INVITED@EXAMPLE.TEST",EmailConfirmed=true};
        InvitationRules.ExistingAccount(u,u.Id,u.NormalizedEmail,false);
        Assert.Throws<UnauthorizedAccessException>(()=>InvitationRules.ExistingAccount(u,null,u.NormalizedEmail,false));
        Assert.Throws<UnauthorizedAccessException>(()=>InvitationRules.ExistingAccount(u,Guid.NewGuid(),u.NormalizedEmail,false));
        Assert.Throws<UnauthorizedAccessException>(()=>InvitationRules.ExistingAccount(u,u.Id,"OTHER@EXAMPLE.TEST",false));
        Assert.Throws<InvalidOperationException>(()=>InvitationRules.ExistingAccount(u,u.Id,u.NormalizedEmail,true));
        u.EmailConfirmed=false;Assert.Throws<UnauthorizedAccessException>(()=>InvitationRules.ExistingAccount(u,u.Id,u.NormalizedEmail,false));
    }
    [Theory]
    [InlineData(OrganizationRole.Student)] [InlineData(OrganizationRole.Instructor)] [InlineData(OrganizationRole.Admin)]
    public void Matching_membership_is_idempotent_and_conflicting_role_is_preserved(OrganizationRole role)
    {
        InvitationRules.Membership(null,role);InvitationRules.Membership(role,role);
        var different=role==OrganizationRole.Admin ? OrganizationRole.Student : OrganizationRole.Admin;
        Assert.Throws<InvalidOperationException>(()=>InvitationRules.Membership(role,different));
    }
    [Fact]
    public void Model_protects_pending_uniqueness_and_concurrent_updates_without_database_access()
    {
        using var db=TenantSqlTests.CreateContext();var entity=db.Model.FindEntityType(typeof(OrganizationInvitation))!;
        Assert.Contains(entity.GetIndexes(),i=>i.IsUnique && i.GetFilter()=="[Pending] = 1" && i.Properties.Select(p=>p.Name).SequenceEqual(new[]{"OrganizationId","NormalizedEmail"}));
        Assert.Contains(entity.GetIndexes(),i=>i.IsUnique && i.Properties.Count==1 && i.Properties[0].Name=="TokenHash");
        Assert.True(entity.FindProperty("Version")!.IsConcurrencyToken);
        Assert.All(entity.GetForeignKeys(),k=>Assert.Equal(DeleteBehavior.Restrict,k.DeleteBehavior));
        Assert.False(db.Database.HasPendingModelChanges());
    }
    [Fact]
    public async Task Forgot_responses_are_generic_even_when_delivery_fails()
    {
        var service=new Mock<IAccountOnboarding>();var controller=new AccountRecoveryController(service.Object);
        service.Setup(x=>x.Forgot("unknown@example.test",default)).Returns(Task.CompletedTask);
        var unknown=Assert.IsType<OkObjectResult>(await controller.Forgot(new("unknown@example.test"),default));
        service.Setup(x=>x.Forgot("known@example.test",default)).ThrowsAsync(new AccountDeliveryException());
        var known=Assert.IsType<OkObjectResult>(await controller.Forgot(new("known@example.test"),default));
        Assert.Equal(JsonSerializer.Serialize(unknown.Value),JsonSerializer.Serialize(known.Value));
    }
    [Fact]
    public async Task Delivery_failure_does_not_report_success_and_fake_never_sends()
    {
        var emails=new Emails {Fail=true};
        await Assert.ThrowsAsync<AccountDeliveryException>(()=>emails.SendAsync(new("a@example.test","Invite","sensitive")));
        Assert.Empty(emails.Sent);
        var service=new Mock<IAccountOnboarding>();var org=Guid.NewGuid();
        service.Setup(x=>x.Create(org,"a@example.test",OrganizationRole.Student,false,true,default)).ThrowsAsync(new AccountDeliveryException());
        var controller=new AccountRecoveryController(service.Object);
        var result=Assert.IsType<ObjectResult>(await controller.Create(org,new("a@example.test",OrganizationRole.Student,false,true),default));
        Assert.Equal(503,result.StatusCode);Assert.DoesNotContain("sensitive",JsonSerializer.Serialize(result.Value));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new UnconfiguredAccountEmailSender().SendAsync(new("a","b","c")));
    }
    [Theory][InlineData("Forgot")][InlineData("Reset")][InlineData("Accept")][InlineData("Create")][InlineData("Revoke")]
    public void API_mutations_require_antiforgery(string name)
        => Assert.NotNull(typeof(AccountRecoveryController).GetMethod(name)!.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    [Theory][InlineData("Reset")][InlineData("Accept")]
    public void Token_mutations_are_POST_only(string name)
    {var method=typeof(AccountRecoveryController).GetMethod(name)!;Assert.NotNull(method.GetCustomAttribute<HttpPostAttribute>());Assert.Null(method.GetCustomAttribute<HttpGetAttribute>());}
    [Fact]
    public void Public_registration_returns_forbidden_without_creating_any_account()
    {
        var controller=new UsersController(null!,null!,null!);
        Assert.Equal(403,Assert.IsType<ObjectResult>(controller.Register()).StatusCode);
    }
    [Fact]
    public void Legacy_and_stale_stamps_require_fresh_login()
    {
        Assert.False(InvitationRules.StampMatches(null,"stamp"));Assert.False(InvitationRules.StampMatches("", "stamp"));
        Assert.False(InvitationRules.StampMatches("old","new"));Assert.False(InvitationRules.StampMatches("stamp",null));
        Assert.True(InvitationRules.StampMatches("new","new"));
    }
    [Fact]
    public void Acceptance_session_keeps_raw_token_out_of_cookie_and_redirect()
    {
        using var cache=new MemoryCache(new MemoryCacheOptions());
        var links=new AccountLinks(cache,new EphemeralDataProtectionProvider());var http=new DefaultHttpContext();
        links.Store(http,"invite","private-invitation-token","");
        var cookie=http.Response.Headers.SetCookie.ToString();Assert.DoesNotContain("private-invitation-token",cookie);
        Assert.Contains("httponly",cookie,StringComparison.OrdinalIgnoreCase);Assert.Contains("secure",cookie,StringComparison.OrdinalIgnoreCase);
        http.Request.Headers.Cookie=cookie.Split(';')[0];Assert.Equal("private-invitation-token",links.Require(http,"invite").Token);
        links.Remove(http,"invite");Assert.Throws<WebApiException>(()=>links.Require(http,"invite"));
    }
    private static (UserManager<ApplicationUser>,ApplicationUser,IDataProtectionProvider) Identity()
    {
        var store=new Mock<IUserPasswordStore<ApplicationUser>>();
        var stamps=store.As<IUserSecurityStampStore<ApplicationUser>>();
        store.Setup(x=>x.GetUserIdAsync(It.IsAny<ApplicationUser>(),It.IsAny<CancellationToken>())).Returns((ApplicationUser u,CancellationToken _)=>Task.FromResult(u.Id.ToString()));
        store.Setup(x=>x.GetUserNameAsync(It.IsAny<ApplicationUser>(),It.IsAny<CancellationToken>())).Returns((ApplicationUser u,CancellationToken _)=>Task.FromResult<string?>(u.UserName));
        store.Setup(x=>x.GetPasswordHashAsync(It.IsAny<ApplicationUser>(),It.IsAny<CancellationToken>())).Returns((ApplicationUser u,CancellationToken _)=>Task.FromResult(u.PasswordHash));
        store.Setup(x=>x.SetPasswordHashAsync(It.IsAny<ApplicationUser>(),It.IsAny<string>(),It.IsAny<CancellationToken>())).Callback((ApplicationUser u,string h,CancellationToken _)=>u.PasswordHash=h).Returns(Task.CompletedTask);
        store.Setup(x=>x.CreateAsync(It.IsAny<ApplicationUser>(),It.IsAny<CancellationToken>())).ReturnsAsync(IdentityResult.Success);
        store.Setup(x=>x.UpdateAsync(It.IsAny<ApplicationUser>(),It.IsAny<CancellationToken>())).ReturnsAsync(IdentityResult.Success);
        stamps.Setup(x=>x.GetSecurityStampAsync(It.IsAny<ApplicationUser>(),It.IsAny<CancellationToken>())).Returns((ApplicationUser u,CancellationToken _)=>Task.FromResult<string?>(u.SecurityStamp));
        stamps.Setup(x=>x.SetSecurityStampAsync(It.IsAny<ApplicationUser>(),It.IsAny<string>(),It.IsAny<CancellationToken>())).Callback((ApplicationUser u,string s,CancellationToken _)=>u.SecurityStamp=s).Returns(Task.CompletedTask);
        var options=new IdentityOptions();options.Password.RequiredLength=8;
        var manager=new UserManager<ApplicationUser>(store.Object,Options.Create(options),new PasswordHasher<ApplicationUser>(),[],[new PasswordValidator<ApplicationUser>()],new UpperInvariantLookupNormalizer(),new IdentityErrorDescriber(),null!,NullLogger<UserManager<ApplicationUser>>.Instance);
        var protection=new EphemeralDataProtectionProvider();
        manager.RegisterTokenProvider(TokenOptions.DefaultProvider,new DataProtectorTokenProvider<ApplicationUser>(protection,Options.Create(new DataProtectionTokenProviderOptions {TokenLifespan=TimeSpan.FromHours(1)}),NullLogger<DataProtectorTokenProvider<ApplicationUser>>.Instance));
        return(manager,new ApplicationUser {Id=Guid.NewGuid(),UserName="invited@example.test",Email="invited@example.test"},protection);
    }
    [Fact]
    public async Task Identity_account_creation_requires_valid_password_and_starts_unconfirmed()
    {
        var (manager,user,_)=Identity();using(manager)
        {
            Assert.False((await manager.CreateAsync(user,"weak")).Succeeded);Assert.False(user.EmailConfirmed);
            Assert.True((await manager.CreateAsync(user,"Valid!Pass123")).Succeeded);Assert.False(user.EmailConfirmed);
            Assert.True(await manager.CheckPasswordAsync(user,"Valid!Pass123"));Assert.NotEqual("Valid!Pass123",user.PasswordHash);
        }
    }
    [Fact]
    public async Task Identity_reset_validates_password_and_consumes_token_by_rotating_stamp()
    {
        var (manager,user,_)=Identity();using(manager)
        {
            Assert.True((await manager.CreateAsync(user,"Valid!Pass123")).Succeeded);
            var old=user.SecurityStamp;var token=await manager.GeneratePasswordResetTokenAsync(user);
            Assert.False((await manager.ResetPasswordAsync(user,token,"weak")).Succeeded);Assert.Equal(old,user.SecurityStamp);
            Assert.False((await manager.ResetPasswordAsync(user,"invalid-token","Another!Pass123")).Succeeded);
            Assert.True((await manager.ResetPasswordAsync(user,token,"Another!Pass123")).Succeeded);
            Assert.NotEqual(old,user.SecurityStamp);Assert.False(InvitationRules.StampMatches(old,user.SecurityStamp));
            Assert.False((await manager.ResetPasswordAsync(user,token,"Third!Pass123")).Succeeded);
            Assert.True(await manager.CheckPasswordAsync(user,"Another!Pass123"));Assert.False(await manager.CheckPasswordAsync(user,"Valid!Pass123"));
        }
    }
    [Fact]
    public async Task Identity_provider_rejects_token_created_over_one_hour_ago()
    {
        var (manager,user,protection)=Identity();using(manager)
        {
            await manager.CreateAsync(user,"Valid!Pass123");
            using var stream=new MemoryStream();using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))
            {writer.Write(DateTimeOffset.UtcNow.AddHours(-2).UtcTicks);writer.Write(user.Id.ToString());writer.Write("ResetPassword");writer.Write(user.SecurityStamp!);}
            var expired=Convert.ToBase64String(protection.CreateProtector("DataProtectorTokenProvider").Protect(stream.ToArray()));
            Assert.False((await manager.ResetPasswordAsync(user,expired,"Another!Pass123")).Succeeded);
        }
    }
    [Fact]
    public async Task Session_validation_fails_when_API_rejects_revoked_JWT()
    {
        var handler=new RejectedSession();using var http=new HttpClient(handler){BaseAddress=new Uri("https://api.example.test")};
        Assert.False(await new LearnerApi(http).SessionValid(new LearnerSession {AccessToken="old-token"},default));
        Assert.Equal("Bearer old-token",handler.Authorization);
    }
    private sealed class RejectedSession : HttpMessageHandler
    {
        public string? Authorization;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {Authorization=request.Headers.Authorization?.ToString();return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized));}
    }
}

