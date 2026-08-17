using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindMap.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMindMapShare : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mindmap_shares",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    MindMapId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ShareToken = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Password = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExpiresAt = table.Column<DateTime>(type: "datetime(3)", nullable: true),
                    MaxAccessCount = table.Column<int>(type: "int", nullable: true),
                    AccessCount = table.Column<int>(type: "int", nullable: false),
                    AllowCopy = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsDisabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedById = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(3)", nullable: false),
                    LastAccessedAt = table.Column<DateTime>(type: "datetime(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mindmap_shares", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mindmap_shares_mindmaps_MindMapId",
                        column: x => x.MindMapId,
                        principalTable: "mindmaps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_mindmap_shares_MindMapId_CreatedAt",
                table: "mindmap_shares",
                columns: new[] { "MindMapId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_mindmap_shares_ShareToken",
                table: "mindmap_shares",
                column: "ShareToken",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mindmap_shares");
        }
    }
}
