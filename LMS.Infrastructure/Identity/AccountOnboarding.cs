using System.Data;
using System.Net.Mail;
using System.Security.Cryptography;
using LMS.Application.Features.Organizations;
using LMS.Domain.Entities;
using LMS.Domain.Enums;
using LMS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Text;
using Microsoft.Extensions.Logging;

namespace LMS.Infrastructure.Identity;

public sealed record InvitationRow(Guid Id, Guid OrganizationId, string Email, OrganizationRole Role,
    DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? AcceptedAt, DateTimeOffset? RevokedAt, DateTimeOffset? DeliveredAt);
public sealed record InvitationPage(int Page, int PageSize, int Total, InvitationRow[] Items);
public sealed record InvitationPreview(string Email, string Organization, OrganizationRole Role, bool ExistingAccount);
public sealed record AcceptInvitation(string Token, string FirstName = "", string LastName = "", string Password = "");

public interface IAccountOnboarding
{
    Task<InvitationRow> Create(Guid org,string email,OrganizationRole role,bool reissue,bool confirmed,CancellationToken ct);
    Task<InvitationPage> List(Guid org,int page,int size,CancellationToken ct);
    Task Revoke(Guid org,Guid id,bool confirmed,CancellationToken ct);
    Task<InvitationPreview> Preview(string raw,CancellationToken ct);
    Task Accept(AcceptInvitation request,Guid? actor,CancellationToken ct);
    Task Forgot(string email,CancellationToken ct);
    Task<bool> Reset(string email,string encoded,string password,CancellationToken ct);
}
public sealed class AccountOnboarding(LMSDbContext db, UserManager<ApplicationUser> users,
    OrganizationContext context, IAccountEmailSender sender, IConfiguration configuration, TimeProvider clock,
    ILogger<AccountOnboarding>? logger = null,
    LMS.Application.Features.Auditing.IAuditObservationWriter? observations = null) : IAccountOnboarding
{
    private DateTimeOffset Now => clock.GetUtcNow();
    private static InvitationRow Row(OrganizationInvitation i) => new(i.Id,i.OrganizationId,i.Email,i.Role,i.CreatedAt,i.ExpiresAt,i.AcceptedAt,i.RevokedAt,i.DeliveredAt);
    private string Link(string path, string token, string? email = null)
    {
        var origin=AccountLinkPolicy.TrustedOrigin(configuration["AccountLinks:PublicUrl"]);
        path=AccountLinkPolicy.Destination(path,configuration["AccountLinks:Destination"]);
        return new Uri(origin,path).AbsoluteUri + "?token=" + Uri.EscapeDataString(token)
            + (email is null ? "" : "&email=" + Uri.EscapeDataString(email));
    }
    private async Task<bool> Platform(Guid id, CancellationToken ct) => await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id
        where ur.UserId == id && r.NormalizedName == "SUPERADMIN" select ur.UserId).AnyAsync(ct);
    private async Task Authority(Guid org, Guid actor, CancellationToken ct)
    {
        var active=await db.Organizations.AsNoTracking().AnyAsync(x=>x.Id==org && x.IsActive,ct);
        var platform=await Platform(actor,ct);
        var admin=await db.OrganizationMemberships.AsNoTracking().AnyAsync(x=>x.OrganizationId==org && x.UserId==actor && x.Role==OrganizationRole.Admin,ct);
        InvitationRules.Authority(active,platform,admin);
    }
    private async Task Admin(Guid org, CancellationToken ct)
    {
        InvitationRules.SelectedOrganization(context.OrganizationId,org,context.UserId);
        await Authority(org,context.UserId,ct);
    }
    public async Task<InvitationRow> Create(Guid org, string email, OrganizationRole role, bool reissue, bool confirmed, CancellationToken ct)
    {
        if(!confirmed || !Enum.IsDefined(role))throw new ArgumentException("Confirm a valid organization invitation.");
        if(string.IsNullOrWhiteSpace(email))throw new ArgumentException("Enter a valid email.");
        email=email.Trim();
        if(email.Length>256 || !MailAddress.TryCreate(email,out var address) || address.Address!=email)throw new ArgumentException("Enter a valid email.");
        var normalized=users.NormalizeEmail(email)!;
        var raw=WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var link=Link("account/invitation",raw);
        OrganizationInvitation invitation;
        Guid? replaced=null;
        await using(var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct))
        {
            await db.OrganizationWriteLock(org).ToListAsync(ct);
            await Admin(org,ct);
            var old=await db.Set<OrganizationInvitation>().SingleOrDefaultAsync(x=>x.OrganizationId==org && x.NormalizedEmail==normalized && x.Pending,ct);
            if(old is not null)
            {
                if(old.Usable(Now) && !reissue)throw new InvalidOperationException("A pending invitation exists. Confirm reissue to replace it.");
                old.Revoke(Now);
                replaced=old.Id;
                await db.SaveChangesAsync(ct); // Release filtered unique slot before inserting replacement.
            }
            invitation=new() {OrganizationId=org,InviterId=context.UserId,Email=email,NormalizedEmail=normalized,Role=role,
                TokenHash=OrganizationInvitation.Hash(raw),CreatedAt=Now,ExpiresAt=Now.AddHours(48)};
            db.Stage(replaced.HasValue?AuditAction.InvitationReissued:AuditAction.InvitationCreated,invitation.Id,org,
                replaced.HasValue?AuditMetadata.Reissue(role,replaced.Value):AuditMetadata.InvitationRole(role));
            db.Add(invitation);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
        }
        // Recoverable delivery: committed undelivered rows remain visible. Reissue rotates the token.
        // No retry/replay and no claim that email and database commits are atomic.
        try { await sender.SendAsync(new(email,"EnterpriseLMS invitation",link),ct); }
        catch (Exception)
        {
            await DeliveryObservation(invitation,sender is UnconfiguredAccountEmailSender?AuditOutcome.Failed:AuditOutcome.Unknown);
            logger?.LogWarning("Invitation email delivery unconfirmed for invitation {InvitationId}.",invitation.Id);
            throw new AccountDeliveryException();
        }
        await DeliveryObservation(invitation,AuditOutcome.Succeeded);
        invitation.DeliveredAt=Now;await db.SaveChangesAsync(ct);
        return Row(invitation);
    }
    public async Task<InvitationPage> List(Guid org,int page,int size,CancellationToken ct)
    {
        await Admin(org,ct);page=Math.Clamp(page,1,100000);size=Math.Clamp(size,1,100);
        var q=db.Set<OrganizationInvitation>().AsNoTracking().Where(x=>x.OrganizationId==org);
        var count=await q.CountAsync(ct);
        var items=await q.OrderByDescending(x=>x.CreatedAt).ThenBy(x=>x.Id).Skip((page-1)*size).Take(size).ToArrayAsync(ct);
        return new(page,size,count,items.Select(Row).ToArray());
    }
    public async Task Revoke(Guid org,Guid id,bool confirmed,CancellationToken ct)
    {
        if(!confirmed)throw new ArgumentException("Confirm revocation.");
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        await db.OrganizationWriteLock(org).ToListAsync(ct);await Admin(org,ct);
        var i=await db.Set<OrganizationInvitation>().SingleOrDefaultAsync(x=>x.Id==id && x.OrganizationId==org,ct) ?? throw new InvalidOperationException("Invitation unavailable.");
        if(i.Pending){i.Revoke(Now);db.Stage(AuditAction.InvitationRevoked,i.Id,i.OrganizationId,AuditMetadata.Empty());await db.SaveChangesAsync(ct);}
        await tx.CommitAsync(ct);
    }
    private async Task<OrganizationInvitation> Find(string raw,CancellationToken ct)
    {
        if(string.IsNullOrEmpty(raw) || raw.Length>256)throw new ArgumentException("Invitation unavailable.");
        var hash=OrganizationInvitation.Hash(raw);
        var i=await db.Set<OrganizationInvitation>().AsNoTracking().SingleOrDefaultAsync(x=>x.TokenHash==hash,ct);
        if(i is null || !i.Usable(Now))throw new ArgumentException("Invitation unavailable.");
        return i;
    }
    public async Task<InvitationPreview> Preview(string raw,CancellationToken ct)
    {
        var i=await Find(raw,ct);await Authority(i.OrganizationId,i.InviterId,ct);
        var name=await db.Organizations.Where(x=>x.Id==i.OrganizationId).Select(x=>x.Name).SingleAsync(ct);
        return new(i.Email,name,i.Role,await db.Users.AnyAsync(x=>x.NormalizedEmail==i.NormalizedEmail,ct));
    }
    public async Task Accept(AcceptInvitation request,Guid? actor,CancellationToken ct)
    {
        if(request.Password is null || request.Password.Length>1024)throw new ArgumentException("Password validation failed.");
        var initial=await Find(request.Token,ct);
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        await db.OrganizationWriteLock(initial.OrganizationId).ToListAsync(ct);
        // A transaction-owned application lock serializes exact-email assignment across organizations
        // and invitation account creation without requiring a destructive unique-email migration.
        var resource="lms-account:"+OrganizationInvitation.Hash(initial.NormalizedEmail);
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r = sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; IF @r < 0 THROW 51000, 'Account operation busy.', 1;",ct);
        var current=await Find(request.Token,ct);
        await Authority(current.OrganizationId,current.InviterId,ct);
        var matches=await db.Users.Where(x=>x.NormalizedEmail==current.NormalizedEmail).Take(2).ToArrayAsync(ct);
        if(matches.Length>1)throw new InvalidOperationException("Account identity is ambiguous. Contact an administrator.");
        ApplicationUser user;
        if(matches.Length==1)
        {
            user=matches[0];
            InvitationRules.ExistingAccount(user,actor,current.NormalizedEmail,await Platform(user.Id,ct));
        }
        else
        {
            if(actor.HasValue)throw new UnauthorizedAccessException("Sign out before establishing the invited account.");
            if(string.IsNullOrWhiteSpace(request.FirstName) || request.FirstName.Length>100 || string.IsNullOrWhiteSpace(request.LastName) || request.LastName.Length>100)
                throw new ArgumentException("Enter your name.");
            user=new() {Id=Guid.NewGuid(),UserName=current.Email,Email=current.Email,FirstName=request.FirstName.Trim(),LastName=request.LastName.Trim()};
            var result=await users.CreateAsync(user,request.Password);
            if(!result.Succeeded)throw new ArgumentException("Password or account validation failed. Use at least eight characters, upper and lower case, a digit and a symbol.");
        }
        var membership=await db.OrganizationMemberships.AsNoTracking().SingleOrDefaultAsync(x=>x.OrganizationId==current.OrganizationId && x.UserId==user.Id,ct);
        InvitationRules.Membership(membership?.Role,current.Role);
        if(membership is null)
        {
            var added=new OrganizationMembership(current.OrganizationId,user.Id,current.Role);
            db.OrganizationMemberships.Add(added);
            db.Stage(AuditAction.MembershipCreated,added.Id,current.OrganizationId,AuditMetadata.Roles(null,current.Role,user.Id));
            // Anonymous invite acceptance proves email access, not prior authenticated actor identity.
        }
        db.Stage(AuditAction.InvitationAccepted,current.Id,current.OrganizationId,AuditMetadata.Related(user.Id));
        // Only successful acceptance proves invited-email access. All writes share the Identity context/transaction.
        user.EmailConfirmed=true;
        var tracked=await db.Set<OrganizationInvitation>().SingleAsync(x=>x.Id==current.Id,ct);
        tracked.Consume(Now);
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
    public const string ForgotResponse="If the account is eligible, password recovery instructions will be sent.";
    public async Task Forgot(string email,CancellationToken ct)
    {
        // Validate link configuration independently of whether an account exists.
        Link("account/reset-password","configuration-check");
        if(string.IsNullOrWhiteSpace(email) || email.Length>256)return;
        var normalized=users.NormalizeEmail(email.Trim());
        var matches=await db.Users.Where(x=>x.NormalizedEmail==normalized).Take(2).ToArrayAsync(ct);
        if(matches.Length!=1)return;
        var user=matches[0];
        var token=await users.GeneratePasswordResetTokenAsync(user);
        var encoded=WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var link=Link("account/reset-password",encoded,user.Email);
        try {await sender.SendAsync(new(user.Email!,"EnterpriseLMS password recovery",link),ct);}
        catch(Exception ex) when(ex is not OperationCanceledException){logger?.LogWarning("Password recovery email delivery failed.");throw new AccountDeliveryException();}
    }
    public async Task<bool> Reset(string email,string encoded,string password,CancellationToken ct)
    {
        if(string.IsNullOrEmpty(encoded) || encoded.Length>4096 || string.IsNullOrEmpty(email) || email.Length>256 || string.IsNullOrEmpty(password) || password.Length>1024)return false;
        string token;
        try {token=Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encoded));}catch(FormatException){return false;}
        var normalized=users.NormalizeEmail(email.Trim());
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        var resource="lms-account:"+OrganizationInvitation.Hash(normalized!);
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r = sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; IF @r < 0 THROW 51000, 'Account operation busy.', 1;",ct);
        var matches=await db.Users.Where(x=>x.NormalizedEmail==normalized).Take(2).ToArrayAsync(ct);
        if(matches.Length!=1)return false;
        // Identity reset validates password/token and rotates SecurityStamp, making the token single-use.
        var result=await users.ResetPasswordAsync(matches[0],token,password);
        if(!result.Succeeded)return false;
        db.Stage(AuditAction.PasswordReset,matches[0].Id,null,AuditMetadata.Empty());
        await db.SaveChangesAsync(ct); // Identity's earlier save is still inside this same outer transaction.
        await tx.CommitAsync(ct);return true;
    }
    private async Task DeliveryObservation(OrganizationInvitation invitation,AuditOutcome outcome)
    {
        if(observations is null)throw new InvalidOperationException("Durable delivery audit writer is not configured.");
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await observations.AppendDelivery(db.Observation(AuditAction.InvitationDelivery,invitation.Id,invitation.OrganizationId,AuditMetadata.Empty(),outcome),deadline.Token);
    }
}
public sealed class AccountDeliveryException : Exception
{
    public AccountDeliveryException() : base("Email delivery was not confirmed. Inspect the invitation list and explicitly reissue if needed.") { }
}
