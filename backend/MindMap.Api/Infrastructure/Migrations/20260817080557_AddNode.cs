using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindMap.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "nodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    MindMapId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ParentId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    Title = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Content = table.Column<string>(type: "longtext", maxLength: 16384, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Note = table.Column<string>(type: "varchar(4096)", maxLength: 4096, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsCollapsed = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    X = table.Column<double>(type: "double", precision: 12, scale: 4, nullable: true),
                    Y = table.Column<double>(type: "double", precision: 12, scale: 4, nullable: true),
                    Width = table.Column<double>(type: "double", precision: 8, scale: 2, nullable: true),
                    Height = table.Column<double>(type: "double", precision: 8, scale: 2, nullable: true),
                    Color = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FontSize = table.Column<int>(type: "int", nullable: true),
                    FontFamily = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Shape = table.Column<int>(type: "int", nullable: true),
                    Icon = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BorderColor = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BackgroundColor = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EdgeColor = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EdgeStyle = table.Column<int>(type: "int", nullable: true),
                    ExtraData = table.Column<string>(type: "longtext", maxLength: 32768, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(3)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_nodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_nodes_mindmaps_MindMapId",
                        column: x => x.MindMapId,
                        principalTable: "mindmaps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_nodes_nodes_ParentId",
                        column: x => x.ParentId,
                        principalTable: "nodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_mindmaps_RootNodeId",
                table: "mindmaps",
                column: "RootNodeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_nodes_MindMapId",
                table: "nodes",
                column: "MindMapId");

            migrationBuilder.CreateIndex(
                name: "IX_nodes_MindMapId_ParentId_SortOrder",
                table: "nodes",
                columns: new[] { "MindMapId", "ParentId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_nodes_ParentId",
                table: "nodes",
                column: "ParentId");

            migrationBuilder.AddForeignKey(
                name: "FK_mindmaps_nodes_RootNodeId",
                table: "mindmaps",
                column: "RootNodeId",
                principalTable: "nodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_mindmaps_nodes_RootNodeId",
                table: "mindmaps");

            migrationBuilder.DropTable(
                name: "nodes");

            migrationBuilder.DropIndex(
                name: "IX_mindmaps_RootNodeId",
                table: "mindmaps");
        }
    }
}
