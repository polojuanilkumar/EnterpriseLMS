using LMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace LMS.Infrastructure.Persistence.Configurations;

public sealed class LessonContentItemConfiguration : IEntityTypeConfiguration<LessonContentItem>
{
    public void Configure(EntityTypeBuilder<LessonContentItem> builder)
    {
        builder.ToTable("LessonContentItems", table =>
        {
            table.HasCheckConstraint("CK_LessonContentItems_Order", "[DisplayOrder] >= 0");
            table.HasCheckConstraint("CK_LessonContentItems_Type", "([Type] = 1 AND [AssetId] IS NULL AND [Text] IS NOT NULL AND [YouTubeVideoId] IS NULL) OR ([Type] = 2 AND [AssetId] IS NULL AND [Text] IS NULL AND LEN([YouTubeVideoId]) = 11 AND [YouTubeVideoId] IS NOT NULL) OR ([Type] IN (3, 4, 5) AND [AssetId] IS NOT NULL AND [Text] IS NULL AND [YouTubeVideoId] IS NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Text).HasColumnType("nvarchar(max)");
        builder.Property(x => x.YouTubeVideoId).HasMaxLength(11);
        builder.HasOne<Lesson>().WithMany().HasForeignKey(x => x.LessonId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<MediaAsset>().WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.LessonId, x.DisplayOrder });
    }
}
