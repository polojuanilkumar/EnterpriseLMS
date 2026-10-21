using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TimestampUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Scope = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ResourceType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Metadata = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                    table.CheckConstraint("CK_AuditEvents_Scope", "([Scope] = 'organization' AND [OrganizationId] IS NOT NULL) OR ([Scope] IN ('platform','account') AND [OrganizationId] IS NULL)");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_ActorUserId_TimestampUtc_Id",
                table: "AuditEvents",
                columns: new[] { "ActorUserId", "TimestampUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_OperationId_Action_ResourceType_ResourceId",
                table: "AuditEvents",
                columns: new[] { "OperationId", "Action", "ResourceType", "ResourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_ResourceId_TimestampUtc_Id",
                table: "AuditEvents",
                columns: new[] { "ResourceId", "TimestampUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_Scope_OrganizationId_TimestampUtc_Id",
                table: "AuditEvents",
                columns: new[] { "Scope", "OrganizationId", "TimestampUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM [AuditEvents]) THROW 51000, 'Audit history must be retained. Downgrade blocked.', 1;");
            migrationBuilder.DropTable(
                name: "AuditEvents");
        }
    }
}
