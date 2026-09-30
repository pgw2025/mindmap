using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindMap.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRecycleBin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RecycleBinRetentionDays",
                table: "users",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "mindmaps",
                type: "datetime(3)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_mindmaps_DeletedAt",
                table: "mindmaps",
                column: "DeletedAt");

            migrationBuilder.CreateIndex(
                name: "IX_mindmaps_OwnerId_DeletedAt",
                table: "mindmaps",
                columns: new[] { "OwnerId", "DeletedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_mindmaps_DeletedAt",
                table: "mindmaps");

            migrationBuilder.DropIndex(
                name: "IX_mindmaps_OwnerId_DeletedAt",
                table: "mindmaps");

            migrationBuilder.DropColumn(
                name: "RecycleBinRetentionDays",
                table: "users");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "mindmaps");
        }
    }
}
