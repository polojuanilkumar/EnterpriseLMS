using LMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace LMS.Infrastructure.Persistence.Configurations;
public sealed class MediaAssetConfiguration : IEntityTypeConfiguration<MediaAsset>
{
    public void Configure(EntityTypeBuilder<MediaAsset> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Ignore(x => x.MediaType);
        builder.ToTable("MediaAssets", t => t.HasCheckConstraint("CK_MediaAssets_TypeSize",
            "([Type] = 3 AND [Size] > 0 AND [Size] <= 26214400) OR ([Type] = 4 AND [Size] > 0 AND [Size] <= 209715200) OR ([Type] = 5 AND [Size] > 0 AND [Size] <= 26214400)"));
        // Retain assets and their lesson ancestry, including after item replacement/deletion.
        builder.HasOne<Lesson>().WithMany().HasForeignKey(x => x.LessonId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}
