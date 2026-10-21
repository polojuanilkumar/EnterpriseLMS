using System.Security.Cryptography;
using System.Text;
using LMS.Domain.Enums;
using LMS.Domain.Entities;
using LMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LMS.Infrastructure.Identity;

public sealed class OrganizationInvitation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid InviterId { get; set; }
    public string Email { get; set; } = "";
    public string NormalizedEmail { get; set; } = "";
    public OrganizationRole Role { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public bool Pending { get; set; } = true;
    public byte[] Version { get; set; } = [];
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public bool Usable(DateTimeOffset now) => Pending && AcceptedAt is null && RevokedAt is null && now < ExpiresAt;
    public void Consume(DateTimeOffset now)
    {
        if(!Usable(now))throw new InvalidOperationException("Invitation unavailable.");
        Pending=false;AcceptedAt=now;
    }
    public void Revoke(DateTimeOffset now)
    {
        if(!Pending)return;
        Pending=false;RevokedAt=now;
    }
}

public sealed class OrganizationInvitationConfiguration : IEntityTypeConfiguration<OrganizationInvitation>
{
    public void Configure(EntityTypeBuilder<OrganizationInvitation> b)
    {
        b.ToTable("OrganizationInvitations");
        b.HasKey(x => x.Id);
        b.Property(x => x.Email).HasMaxLength(256).IsRequired();
        b.Property(x => x.NormalizedEmail).HasMaxLength(256).IsRequired();
        b.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.Version).IsRowVersion();
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => new { x.OrganizationId, x.NormalizedEmail }).IsUnique().HasFilter("[Pending] = 1");
        b.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.InviterId).OnDelete(DeleteBehavior.Restrict);
    }
}
