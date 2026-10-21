using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLessonContentItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LessonContentItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    YouTubeVideoId = table.Column<string>(type: "nvarchar(11)", maxLength: 11, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LessonContentItems", x => x.Id);
                    table.CheckConstraint("CK_LessonContentItems_Order", "[DisplayOrder] >= 0");
                    table.CheckConstraint("CK_LessonContentItems_Type", "([Type] = 1 AND [Text] IS NOT NULL AND [YouTubeVideoId] IS NULL) OR ([Type] = 2 AND [Text] IS NULL AND LEN([YouTubeVideoId]) = 11 AND [YouTubeVideoId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_LessonContentItems_Lessons_LessonId",
                        column: x => x.LessonId,
                        principalTable: "Lessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LessonContentItems_LessonId_DisplayOrder",
                table: "LessonContentItems",
                columns: new[] { "LessonId", "DisplayOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LessonContentItems");
        }
    }
}
