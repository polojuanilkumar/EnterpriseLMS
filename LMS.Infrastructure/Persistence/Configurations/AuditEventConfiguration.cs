using LMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace LMS.Infrastructure.Persistence.Configurations;
public sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> b)
    {
        b.ToTable("AuditEvents",t=>t.HasCheckConstraint("CK_AuditEvents_Scope","([Scope] = 'organization' AND [OrganizationId] IS NOT NULL) OR ([Scope] IN ('platform','account') AND [OrganizationId] IS NULL)"));
        b.HasKey(x=>x.Id);b.Property(x=>x.Scope).HasMaxLength(20).IsRequired();
        b.Property(x=>x.Action).HasMaxLength(64).IsRequired();b.Property(x=>x.ResourceType).HasMaxLength(32).IsRequired();
        b.Property(x=>x.Outcome).HasMaxLength(16).IsRequired();b.Property(x=>x.CorrelationId).HasMaxLength(32).IsRequired();
        b.Property(x=>x.Metadata).HasMaxLength(512).IsRequired();
        b.HasIndex(x=>new {x.Scope,x.OrganizationId,x.TimestampUtc,x.Id});
        b.HasIndex(x=>new {x.ActorUserId,x.TimestampUtc,x.Id});b.HasIndex(x=>new {x.ResourceId,x.TimestampUtc,x.Id});
        b.HasIndex(x=>new {x.OperationId,x.Action,x.ResourceType,x.ResourceId}).IsUnique();
        // Historical scalar IDs intentionally have no FK/cascade to deletable users/resources/organizations.
    }
}
