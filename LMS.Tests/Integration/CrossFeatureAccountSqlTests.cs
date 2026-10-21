using System.Collections.Concurrent;
using System.Text;
using LMS.Application.Features.Auditing;
using LMS.Application.Features.Organizations;
using LMS.Domain.Entities;
using LMS.Domain.Enums;
using LMS.Infrastructure.Identity;
using LMS.Infrastructure.Persistence;
using LMS.Infrastructure.Repositories;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace LMS.Tests.Integration;

public sealed partial class CrossFeatureSqlTests
{
    private readonly IDataProtectionProvider protection = new EphemeralDataProtectionProvider();
    private sealed class Emails : IAccountEmailSender
    {
        public readonly ConcurrentQueue<AccountEmail> Sent = new();
        public Task SendAsync(AccountEmail email, CancellationToken ct = default) { Sent.Enqueue(email); return Task.CompletedTask; }
        public string Token() => Uri.UnescapeDataString(new Uri(Sent.Last().Body).Query.Split("token=")[1].Split('&')[0]);
    }
    private sealed class Delivery(DisposableSqlServer sql) : IAuditObservationWriter
    {
        public async Task AppendDelivery(AuditEvent observed, CancellationToken token)
        { await using var db = sql.Context(); db.AuditEvents.Add(observed); await db.SaveChangesAsync(token); }
    }
    private ServiceProvider Identity(Graph? graph = null, Guid? actor = null, IAccountEmailSender? sender = null, TimeProvider? clock = null)
    {
        var services = new ServiceCollection(); services.AddLogging();
        services.AddSingleton(clock ?? TimeProvider.System); services.AddSingleton(protection);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["AccountLinks:PublicUrl"] = "https://web.example.invalid/", ["Jwt:Key"] = new string('k', 64),
            ["Jwt:Issuer"] = "isolated-tests", ["Jwt:Audience"] = "isolated-tests", ["Jwt:AccessTokenMinutes"] = "15"
        }).Build());
        var selected = graph is null ? new OrganizationContext() : Scope(graph, actor);
        services.AddSingleton(selected);
        services.AddScoped(_ => graph is null ? sql.Context(actor: actor) : sql.Context(graph.Org.Id, actor ?? graph.Admin));
        services.AddIdentityCore<ApplicationUser>(o => { o.User.RequireUniqueEmail = true; o.Password.RequiredLength = 8; })
            .AddRoles<IdentityRole<Guid>>().AddEntityFrameworkStores<LMSDbContext>().AddDefaultTokenProviders();
        services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(1));
        services.AddSingleton<IAccountEmailSender>(sender ?? new Emails());
        services.AddSingleton<IAuditObservationWriter>(new Delivery(sql));
        services.AddScoped<AccountOnboarding>();
        return services.BuildServiceProvider(); // Test composition only; no Program, role seeder, appsettings, real sender or host.
    }

    [SqlIntegrationFact]
    public async Task Invitation_reissue_revocation_and_delivery_have_separate_verified_outcomes()
    {
        var g = await Seed(); var email = Guid.NewGuid().ToString("N") + "@example.invalid"; var emails = new Emails();
        InvitationRow first; string old;
        using (var services = Identity(g, sender: emails))
        { first = await services.GetRequiredService<AccountOnboarding>().Create(g.Org.Id, email, OrganizationRole.Student, false, true, default); old = emails.Token(); }
        InvitationRow second;
        using (var services = Identity(g, sender: emails))
        { second = await services.GetRequiredService<AccountOnboarding>().Create(g.Org.Id, email, OrganizationRole.Student, true, true, default); }
        var replacement = emails.Token();
        using (var services = Identity())
        { await Assert.ThrowsAsync<ArgumentException>(() => services.GetRequiredService<AccountOnboarding>().Preview(old, default)); }
        using (var services = Identity(g))
        { await services.GetRequiredService<AccountOnboarding>().Revoke(g.Org.Id, second.Id, true, default); }
        using (var services = Identity())
        { await Assert.ThrowsAsync<ArgumentException>(() => services.GetRequiredService<AccountOnboarding>().Preview(replacement, default)); }
        await using var db = sql.Context();
        var prior = await db.Set<OrganizationInvitation>().SingleAsync(x => x.Id == first.Id);
        Assert.NotNull(prior.RevokedAt); Assert.NotEqual(old, prior.TokenHash); Assert.False(prior.Pending);
        var created = await db.AuditEvents.SingleAsync(x => x.ResourceId == first.Id && x.Action == "invitation.created");
        var delivered = await db.AuditEvents.SingleAsync(x => x.ResourceId == first.Id && x.Action == "invitation.delivery");
        Assert.NotEqual(created.OperationId, delivered.OperationId); Assert.Equal("succeeded", delivered.Outcome);
    }

    [SqlIntegrationFact]
    public async Task Concurrent_invitation_acceptance_creates_one_confirmed_account_and_membership()
    {
        var g = await Seed(); var emails = new Emails(); var email = Guid.NewGuid().ToString("N") + "@example.invalid";
        using (var services = Identity(g, sender: emails))
        { await services.GetRequiredService<AccountOnboarding>().Create(g.Org.Id, email, OrganizationRole.Student, false, true, default); }
        var token = emails.Token(); var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> Accept()
        {
            using var services = Identity(); await start.Task;
            try { await services.GetRequiredService<AccountOnboarding>().Accept(new(token, "Synthetic", "Learner", "Valid!Pass123"), null, default); return true; }
            catch (ArgumentException) { return false; }
        }
        var a = Accept(); var b = Accept(); start.SetResult();
        Assert.Equal(1, (await Task.WhenAll(a, b).WaitAsync(TimeSpan.FromSeconds(30))).Count(x => x));
        await using var db = sql.Context(); var user = await db.Users.SingleAsync(x => x.NormalizedEmail == email.ToUpperInvariant());
        Assert.True(user.EmailConfirmed); Assert.NotNull(user.PasswordHash);
        Assert.Equal(OrganizationRole.Student, (await db.OrganizationMemberships.SingleAsync(x => x.OrganizationId == g.Org.Id && x.UserId == user.Id)).Role);
        Assert.Equal(1, await db.AuditEvents.CountAsync(x => x.OrganizationId == g.Org.Id && x.Action == "invitation.accepted"));
        using var replay = Identity();
        await Assert.ThrowsAsync<ArgumentException>(() => replay.GetRequiredService<AccountOnboarding>().Accept(new(token, Password: "Another!Pass123"), null, default));
    }

    [SqlIntegrationFact]
    public async Task Existing_account_requires_matching_verified_identity_and_conflict_preserves_membership()
    {
        var g = await Seed(); var emails = new Emails(); string email;
        await using (var db = sql.Context()) { email = (await db.Users.SingleAsync(x => x.Id == g.Student)).Email!; }
        using (var services = Identity(g, sender: emails))
        { await services.GetRequiredService<AccountOnboarding>().Create(g.Org.Id, email, OrganizationRole.Instructor, false, true, default); }
        var token = emails.Token();
        using (var services = Identity())
        { await Assert.ThrowsAsync<UnauthorizedAccessException>(() => services.GetRequiredService<AccountOnboarding>().Accept(new(token), g.Instructor, default)); }
        using (var services = Identity())
        { await Assert.ThrowsAsync<InvalidOperationException>(() => services.GetRequiredService<AccountOnboarding>().Accept(new(token), g.Student, default)); }
        await using var verify = sql.Context();
        Assert.Equal(OrganizationRole.Student, (await verify.OrganizationMemberships.SingleAsync(x => x.UserId == g.Student)).Role);
        Assert.True((await verify.Set<OrganizationInvitation>().SingleAsync(x => x.TokenHash == OrganizationInvitation.Hash(token))).Pending);
    }

    [SqlIntegrationFact]
    public async Task Explicit_delivery_failure_keeps_recoverable_invitation_without_delivered_timestamp()
    {
        var g = await Seed();
        using (var services = Identity(g, sender: new UnconfiguredAccountEmailSender()))
        { await Assert.ThrowsAsync<AccountDeliveryException>(() => services.GetRequiredService<AccountOnboarding>().Create(g.Org.Id, Guid.NewGuid().ToString("N") + "@example.invalid", OrganizationRole.Student, false, true, default)); }
        await using var db = sql.Context();
        var invitation = await db.Set<OrganizationInvitation>().SingleAsync(x => x.OrganizationId == g.Org.Id);
        Assert.True(invitation.Pending); Assert.Null(invitation.DeliveredAt);
        var events = await db.AuditEvents.Where(x => x.ResourceId == invitation.Id).ToArrayAsync();
        Assert.Equal("succeeded", events.Single(x => x.Action == "invitation.created").Outcome);
        Assert.Equal("failed", events.Single(x => x.Action == "invitation.delivery").Outcome);
    }

    [SqlIntegrationFact]
    public async Task Password_reset_checks_identity_password_expiry_reuse_and_rotates_stored_JWT_stamp()
    {
        var g = await Seed(); string email, encoded, jwtStamp;
        using (var services = Identity())
        {
            var manager = services.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await manager.FindByIdAsync(g.Student.ToString()))!; email = user.Email!;
            var jwt = await new JwtTokenService(services.GetRequiredService<IConfiguration>(), manager).GenerateTokenAsync(user.Id, email, []);
            jwtStamp = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(jwt.AccessToken).Claims.Single(x => x.Type == "lms-stamp").Value;
            encoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(await manager.GeneratePasswordResetTokenAsync(user)));
        }
        using (var services = Identity())
        { Assert.False(await services.GetRequiredService<AccountOnboarding>().Reset(email, encoded, "weak", default)); }
        using (var services = Identity())
        { Assert.True(await services.GetRequiredService<AccountOnboarding>().Reset(email, encoded, "Another!Pass123", default)); }
        using (var services = Identity())
        {
            var manager = services.GetRequiredService<UserManager<ApplicationUser>>(); var user = (await manager.FindByEmailAsync(email))!;
            Assert.False(InvitationRules.StampMatches(jwtStamp, await manager.GetSecurityStampAsync(user)));
            Assert.True(await manager.CheckPasswordAsync(user, "Another!Pass123"));
            Assert.False(await services.GetRequiredService<AccountOnboarding>().Reset(email, encoded, "Third!Pass123", default));
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            { writer.Write(DateTimeOffset.UtcNow.AddHours(-2).UtcTicks); writer.Write(user.Id.ToString()); writer.Write("ResetPassword"); writer.Write(user.SecurityStamp!); }
            var expired = Convert.ToBase64String(protection.CreateProtector("DataProtectorTokenProvider").Protect(stream.ToArray()));
            Assert.False(await services.GetRequiredService<AccountOnboarding>().Reset(email, WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(expired)), "Third!Pass123", default));
        }
        await using var db = sql.Context();
        var audit = await db.AuditEvents.SingleAsync(x => x.ResourceId == g.Student && x.Action == "account.password-reset");
        Assert.Null(audit.OrganizationId); Assert.Null(audit.ActorUserId); Assert.Equal("{}", audit.Metadata);
    }
    [SqlIntegrationFact]
    public async Task SQL_reset_invalidates_previously_issued_JWT_and_real_MVC_cookie_on_next_use()
    {
        var g = await Seed();
        using var api = new InProcessMvcTests.FakeApi();
        api.User = g.Student;
        string email, reset;
        using (var services = Identity())
        {
            var manager = services.GetRequiredService<UserManager<ApplicationUser>>();
            // The synthetic web session can represent this existing SQL user without normal API startup.
            var user = (await manager.FindByIdAsync(g.Student.ToString()))!; email = user.Email!;
            api.AccessToken = (await new JwtTokenService(services.GetRequiredService<IConfiguration>(), manager).GenerateTokenAsync(user.Id, email, [])).AccessToken;
            reset = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(await manager.GeneratePasswordResetTokenAsync(user)));
        }
        api.ValidateSession = async jwt =>
        {
            var principal = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ValidateToken(jwt, new TokenValidationParameters {
                ValidateIssuer = true, ValidIssuer = "isolated-tests", ValidateAudience = true, ValidAudience = "isolated-tests",
                ValidateLifetime = true, ClockSkew = TimeSpan.Zero, ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('k', 64)))
            }, out _);
            using var services = Identity();
            return await JwtSecurityStampValidation.ValidAsync(principal, services.GetRequiredService<UserManager<ApplicationUser>>());
        };
        var (app, server) = await InProcessMvcTests.Host(api); await using var application = app;
        var cookie = InProcessMvcTests.AuthCookie(app, api);
        Assert.Equal(200, (await server.Send("/account/organizations", cookie: cookie)).Status);
        using (var services = Identity()) { Assert.True(await services.GetRequiredService<AccountOnboarding>().Reset(email, reset, "Another!Pass123", default)); }
        Assert.False(await api.ValidateSession(api.AccessToken));
        var denied = await server.Send("/account/organizations", cookie: cookie);
        Assert.Equal(302, denied.Status); Assert.Equal("/account/login", denied.Headers.Location.ToString());
        Assert.Equal(302, (await server.Send("/account/organizations", cookie: cookie)).Status);
    }
    [SqlIntegrationFact]
    public async Task Invitation_GET_preserves_state_expiry_boundary_and_revoked_inviter_deny_acceptance()
    {
        var g = await Seed(); var emails = new Emails(); var now = DateTimeOffset.UtcNow;
        var email = Guid.NewGuid().ToString("N") + "@example.invalid";
        using (var services = Identity(g, sender: emails, clock: new FixedClock(now)))
        { await services.GetRequiredService<AccountOnboarding>().Create(g.Org.Id, email, OrganizationRole.Student, false, true, default); }
        var token = emails.Token();
        using (var services = Identity(clock: new FixedClock(now.AddHours(48).AddTicks(-1))))
        { await services.GetRequiredService<AccountOnboarding>().Preview(token, default); }
        using (var services = Identity(clock: new FixedClock(now.AddHours(48))))
        { await Assert.ThrowsAsync<ArgumentException>(() => services.GetRequiredService<AccountOnboarding>().Preview(token, default)); }
        var replacementAdmin = await Member(g, OrganizationRole.Admin);
        await using (var db = sql.Context(g.Org.Id, replacementAdmin))
        { await new OrganizationRepository(db, Scope(g, replacementAdmin)).SetMembershipAsync(g.Org.Id, g.Admin, OrganizationRole.Student); }
        using (var services = Identity(clock: new FixedClock(now.AddMinutes(1))))
        { await Assert.ThrowsAsync<UnauthorizedAccessException>(() => services.GetRequiredService<AccountOnboarding>().Accept(new(token, "Synthetic", "User", "Valid!Pass123"), null, default)); }
        await using var verify = sql.Context();
        Assert.True((await verify.Set<OrganizationInvitation>().SingleAsync(x => x.TokenHash == OrganizationInvitation.Hash(token))).Pending);
        Assert.False(await verify.Users.AnyAsync(x => x.NormalizedEmail == email.ToUpperInvariant()));
        Assert.False(await verify.AuditEvents.AnyAsync(x => x.OrganizationId == g.Org.Id && x.Action == "invitation.accepted"));
    }
    [SqlIntegrationFact]
    public async Task Concurrent_invitation_creation_has_one_pending_row_and_matching_membership_can_accept()
    {
        var g = await Seed(); var emails = new Emails(); string email;
        await using (var db = sql.Context()) { email = (await db.Users.SingleAsync(x => x.Id == g.Student)).Email!; }
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> Create()
        {
            using var services = Identity(g, sender: emails); await start.Task;
            try { await services.GetRequiredService<AccountOnboarding>().Create(g.Org.Id, email, OrganizationRole.Student, false, true, default); return true; }
            catch (InvalidOperationException) { return false; }
        }
        var a = Create(); var b = Create(); start.SetResult();
        Assert.Equal(1, (await Task.WhenAll(a, b).WaitAsync(TimeSpan.FromSeconds(30))).Count(x => x));
        using (var services = Identity(actor: g.Student))
        { await services.GetRequiredService<AccountOnboarding>().Accept(new(emails.Token()), g.Student, default); }
        await using var verify = sql.Context();
        Assert.Equal(1, await verify.Set<OrganizationInvitation>().CountAsync(x => x.OrganizationId == g.Org.Id));
        Assert.Equal(1, await verify.OrganizationMemberships.CountAsync(x => x.OrganizationId == g.Org.Id && x.UserId == g.Student));
        var accepted = await verify.AuditEvents.SingleAsync(x => x.OrganizationId == g.Org.Id && x.Action == "invitation.accepted");
        Assert.Equal(g.Student, accepted.ActorUserId);
    }
}
