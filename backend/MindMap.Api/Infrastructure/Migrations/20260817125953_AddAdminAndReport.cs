using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindMap.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminAndReport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsTakenDown",
                table: "mindmaps",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "TakenDownAt",
                table: "mindmaps",
                type: "datetime(3)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TakenDownReason",
                table: "mindmaps",
                type: "varchar(256)",
                maxLength: 256,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "mindmap_reports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    MindMapId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ReporterId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    Reason = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ResolutionNote = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ResolvedById = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(3)", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "datetime(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mindmap_reports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mindmap_reports_mindmaps_MindMapId",
                        column: x => x.MindMapId,
                        principalTable: "mindmaps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_mindmap_reports_users_ReporterId",
                        column: x => x.ReporterId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_mindmap_reports_users_ResolvedById",
                        column: x => x.ResolvedById,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_mindmap_reports_MindMapId",
                table: "mindmap_reports",
                column: "MindMapId");

            migrationBuilder.CreateIndex(
                name: "IX_mindmap_reports_ReporterId",
                table: "mindmap_reports",
                column: "ReporterId");

            migrationBuilder.CreateIndex(
                name: "IX_mindmap_reports_ResolvedById",
                table: "mindmap_reports",
                column: "ResolvedById");

            migrationBuilder.CreateIndex(
                name: "IX_mindmap_reports_Status_CreatedAt",
                table: "mindmap_reports",
                columns: new[] { "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mindmap_reports");

            migrationBuilder.DropColumn(
                name: "IsTakenDown",
                table: "mindmaps");

            migrationBuilder.DropColumn(
                name: "TakenDownAt",
                table: "mindmaps");

            migrationBuilder.DropColumn(
                name: "TakenDownReason",
                table: "mindmaps");
        }
    }
}
