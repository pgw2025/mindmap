using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindMap.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNodeDirection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Direction",
                table: "nodes",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Direction",
                table: "nodes");
        }
    }
}
