using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPdfPresentations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_MediaAssets_TypeSize",
                table: "MediaAssets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LessonContentItems_Type",
                table: "LessonContentItems");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MediaAssets_TypeSize",
                table: "MediaAssets",
                sql: "([Type] = 3 AND [Size] > 0 AND [Size] <= 26214400) OR ([Type] = 4 AND [Size] > 0 AND [Size] <= 209715200) OR ([Type] = 5 AND [Size] > 0 AND [Size] <= 26214400)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LessonContentItems_Type",
                table: "LessonContentItems",
                sql: "([Type] = 1 AND [AssetId] IS NULL AND [Text] IS NOT NULL AND [YouTubeVideoId] IS NULL) OR ([Type] = 2 AND [AssetId] IS NULL AND [Text] IS NULL AND LEN([YouTubeVideoId]) = 11 AND [YouTubeVideoId] IS NOT NULL) OR ([Type] IN (3, 4, 5) AND [AssetId] IS NOT NULL AND [Text] IS NULL AND [YouTubeVideoId] IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_MediaAssets_TypeSize",
                table: "MediaAssets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LessonContentItems_Type",
                table: "LessonContentItems");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MediaAssets_TypeSize",
                table: "MediaAssets",
                sql: "([Type] = 3 AND [Size] > 0 AND [Size] <= 26214400) OR ([Type] = 4 AND [Size] > 0 AND [Size] <= 209715200)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LessonContentItems_Type",
                table: "LessonContentItems",
                sql: "([Type] = 1 AND [AssetId] IS NULL AND [Text] IS NOT NULL AND [YouTubeVideoId] IS NULL) OR ([Type] = 2 AND [AssetId] IS NULL AND [Text] IS NULL AND LEN([YouTubeVideoId]) = 11 AND [YouTubeVideoId] IS NOT NULL) OR ([Type] IN (3, 4) AND [AssetId] IS NOT NULL AND [Text] IS NULL AND [YouTubeVideoId] IS NULL)");
        }
    }
}
