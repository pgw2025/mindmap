using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindMap.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNodeSortOrderUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_nodes_MindMapId_ParentId_SortOrder",
                table: "nodes");

            // 建立唯一索引前，先对存量数据去重：按 (MindMapId, ParentId) 分组，
            // 依据 SortOrder、CreatedAt 重排 SortOrder 为连续序号，避免唯一索引创建失败。
            migrationBuilder.Sql(
                """
                UPDATE nodes n
                JOIN (
                    SELECT Id,
                           ROW_NUMBER() OVER (
                               PARTITION BY MindMapId, ParentId
                               ORDER BY SortOrder ASC, CreatedAt ASC, Id ASC
                           ) - 1 AS NewSortOrder
                    FROM nodes
                ) r ON n.Id = r.Id
                SET n.SortOrder = r.NewSortOrder
                WHERE n.SortOrder <> r.NewSortOrder;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_nodes_MindMapId_ParentId_SortOrder",
                table: "nodes",
                columns: new[] { "MindMapId", "ParentId", "SortOrder" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_nodes_MindMapId_ParentId_SortOrder",
                table: "nodes");

            migrationBuilder.CreateIndex(
                name: "IX_nodes_MindMapId_ParentId_SortOrder",
                table: "nodes",
                columns: new[] { "MindMapId", "ParentId", "SortOrder" });
        }
    }
}
