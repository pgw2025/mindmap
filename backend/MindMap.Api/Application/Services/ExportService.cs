using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using MindMap.Api.Common.Exceptions;
using MindMap.Api.Infrastructure.Data;

namespace MindMap.Api.Application.Services;

public interface IExportService
{
    /// <summary>导出导图为 FreeMind (.mm) XML 格式。</summary>
    Task<string> ExportFreeMindAsync(Guid? userId, Guid mindMapId, CancellationToken ct = default);
}

/// <summary>
/// 导出服务：将思维导图导出为 FreeMind (.mm) 等 XML 格式。
/// </summary>
public class ExportService : IExportService
{
    private readonly AppDbContext _db;

    public ExportService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<string> ExportFreeMindAsync(Guid? userId, Guid mindMapId, CancellationToken ct = default)
    {
        // 查询导图（AsNoTracking 只读）
        var map = await _db.MindMaps.AsNoTracking().FirstOrDefaultAsync(m => m.Id == mindMapId, ct)
            ?? throw ApiException.NotFound("导图", mindMapId);

        // 权限检查：非所有者且非公开则禁止
        if (!map.IsPublic && (userId is null || map.OwnerId != userId.Value))
            throw ApiException.Forbidden("无权操作");

        // 查询该导图全部节点（扁平），按根优先 + 排序
        var nodes = await _db.Nodes.AsNoTracking()
            .Where(n => n.MindMapId == mindMapId)
            .OrderBy(n => n.ParentId == null ? 0 : 1)
            .ThenBy(n => n.SortOrder)
            .ToListAsync(ct);

        // 按 ParentId 建立查找表，便于递归构建子树
        var lookup = nodes.ToLookup(n => n.ParentId);
        var root = lookup[null].FirstOrDefault();

        var xmlMap = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement("map", new XAttribute("version", "1.0.1")));

        if (root is not null)
        {
            xmlMap.Root!.Add(BuildNode(root, lookup, isRoot: true));
        }

        return ToXmlString(xmlMap);
    }

    /// <summary>
    /// 递归构建 FreeMind node 元素。
    /// 根节点不设 POSITION；子节点交替设 POSITION="right"/"left"。
    /// XElement/XAttribute 会自动对 &、<、>、"、' 进行 XML 转义。
    /// </summary>
    private static XElement BuildNode(MindMap.Api.Domain.Entities.Node node, ILookup<Guid?, MindMap.Api.Domain.Entities.Node> lookup, bool isRoot)
    {
        var el = new XElement("node");

        // TEXT = Title
        el.SetAttributeValue("TEXT", string.IsNullOrEmpty(node.Title) ? string.Empty : node.Title);

        // 根节点不设 POSITION；子节点默认 right（由父节点循环覆盖为交替值）
        if (!isRoot)
        {
            el.SetAttributeValue("POSITION", "right");
        }

        // COLOR = Color
        if (!string.IsNullOrEmpty(node.Color))
            el.SetAttributeValue("COLOR", node.Color);

        // BACKGROUND_COLOR = BackgroundColor
        if (!string.IsNullOrEmpty(node.BackgroundColor))
            el.SetAttributeValue("BACKGROUND_COLOR", node.BackgroundColor);

        // 折叠状态
        if (node.IsCollapsed)
            el.SetAttributeValue("FOLDED", "true");

        // 递归构建子节点（按 SortOrder 排序，交替 right/left）
        var children = lookup[node.Id].OrderBy(n => n.SortOrder).ToList();
        for (var i = 0; i < children.Count; i++)
        {
            var childEl = BuildNode(children[i], lookup, isRoot: false);
            childEl.SetAttributeValue("POSITION", i % 2 == 0 ? "right" : "left");
            el.Add(childEl);
        }

        return el;
    }

    /// <summary>
    /// 将 XDocument 序列化为带 XML 声明的字符串（UTF-8 无 BOM，缩进两空格）。
    /// </summary>
    private static string ToXmlString(XDocument doc)
    {
        var sb = new StringBuilder();
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            OmitXmlDeclaration = false,
            Indent = true,
            IndentChars = "  "
        };
        using (var writer = XmlWriter.Create(sb, settings))
        {
            doc.Save(writer);
        }
        return sb.ToString();
    }
}
