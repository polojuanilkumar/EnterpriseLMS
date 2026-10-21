using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPrivateLessonMedia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_LessonContentItems_Type",
                table: "LessonContentItems");

            migrationBuilder.AddColumn<Guid>(
                name: "AssetId",
                table: "LessonContentItems",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MediaAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaAssets", x => x.Id);
                    table.CheckConstraint("CK_MediaAssets_TypeSize", "([Type] = 3 AND [Size] > 0 AND [Size] <= 26214400) OR ([Type] = 4 AND [Size] > 0 AND [Size] <= 209715200)");
                    table.ForeignKey(
                        name: "FK_MediaAssets_Lessons_LessonId",
                        column: x => x.LessonId,
                        principalTable: "Lessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MediaAssets_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LessonContentItems_AssetId",
                table: "LessonContentItems",
                column: "AssetId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LessonContentItems_Type",
                table: "LessonContentItems",
                sql: "([Type] = 1 AND [AssetId] IS NULL AND [Text] IS NOT NULL AND [YouTubeVideoId] IS NULL) OR ([Type] = 2 AND [AssetId] IS NULL AND [Text] IS NULL AND LEN([YouTubeVideoId]) = 11 AND [YouTubeVideoId] IS NOT NULL) OR ([Type] IN (3, 4) AND [AssetId] IS NOT NULL AND [Text] IS NULL AND [YouTubeVideoId] IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_LessonId",
                table: "MediaAssets",
                column: "LessonId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_OrganizationId",
                table: "MediaAssets",
                column: "OrganizationId");

            migrationBuilder.AddForeignKey(
                name: "FK_LessonContentItems_MediaAssets_AssetId",
                table: "LessonContentItems",
                column: "AssetId",
                principalTable: "MediaAssets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LessonContentItems_MediaAssets_AssetId",
                table: "LessonContentItems");

            migrationBuilder.DropTable(
                name: "MediaAssets");

            migrationBuilder.DropIndex(
                name: "IX_LessonContentItems_AssetId",
                table: "LessonContentItems");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LessonContentItems_Type",
                table: "LessonContentItems");

            migrationBuilder.DropColumn(
                name: "AssetId",
                table: "LessonContentItems");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LessonContentItems_Type",
                table: "LessonContentItems",
                sql: "([Type] = 1 AND [Text] IS NOT NULL AND [YouTubeVideoId] IS NULL) OR ([Type] = 2 AND [Text] IS NULL AND LEN([YouTubeVideoId]) = 11 AND [YouTubeVideoId] IS NOT NULL)");
        }
    }
}
