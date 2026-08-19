using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using MindMap.Api.Application.DTOs.MindMaps;
using MindMap.Api.Common.Exceptions;
using MindMap.Api.Domain.Entities;
using MindMap.Api.Domain.Entities.Enums;
using MindMap.Api.Infrastructure.Data;

namespace MindMap.Api.Application.Services;

public interface IImportService
{
    /// <summary>从上传的文件导入导图，返回新建的导图详情。</summary>
    Task<MindMapDetailDto> ImportAsync(
        Guid userId,
        IFormFile file,
        string title,
        Guid? folderId,
        string? theme,
        MindMapLayout defaultLayout,
        CancellationToken ct = default);
}

/// <summary>
/// 导图导入服务。支持格式：
/// - FreeMind (.mm) XML
/// - simple-mind-map 原生 JSON (.json / .smm)
/// - Markdown 大纲 (.md)
/// - XMind (.xmind, ZIP 内含 content.json)
/// </summary>
public class ImportService : IImportService
{
    private const int MaxNodesAllowed = 2000;
    private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5MB

    private readonly AppDbContext _db;

    public ImportService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<MindMapDetailDto> ImportAsync(
        Guid userId,
        IFormFile file,
        string title,
        Guid? folderId,
        string? theme,
        MindMapLayout defaultLayout,
        CancellationToken ct = default)
    {
        // ---------- 基础校验 ----------
        if (file is null || file.Length == 0)
            throw ApiException.BadRequest("请选择文件");
        if (file.Length > MaxFileSizeBytes)
            throw ApiException.BadRequest($"文件过大，上限 {MaxFileSizeBytes / 1024 / 1024}MB");

        var fileName = file.FileName ?? string.Empty;
        var ext = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();

        // 标题：优先用户输入，否则取文件名去扩展名
        var mapTitle = string.IsNullOrWhiteSpace(title)
            ? (string.IsNullOrWhiteSpace(fileName) ? "未命名导图" : Path.GetFileNameWithoutExtension(fileName))
            : title.Trim();
        if (mapTitle.Length > 128) mapTitle = mapTitle[..125] + "...";

        // ---------- 读取文件流 ----------
        using var stream = file.OpenReadStream();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();

        // ---------- 分发解析器 ----------
        ParsedNode? root = ext switch
        {
            "mm" => ParseFreeMind(bytes),
            "json" or "smm" => ParseSimpleMindMapJson(bytes),
            "md" or "markdown" => ParseMarkdown(bytes),
            "xmind" => ParseXMind(bytes),
            _ => TryDetectAndParse(bytes)
        };

        if (root is null)
            throw ApiException.BadRequest("无法识别的文件格式或文件内容为空");

        // 节点计数校验
        var totalNodes = CountNodes(root);
        if (totalNodes > MaxNodesAllowed)
            throw ApiException.BadRequest($"节点过多（{totalNodes}），单次导入上限 {MaxNodesAllowed} 个");

        // ---------- 文件夹 & 标签校验（复用 MindMapService 逻辑） ----------
        if (folderId.HasValue)
        {
            var folderOwned = await _db.Folders.AnyAsync(f => f.Id == folderId.Value && f.UserId == userId, ct);
            if (!folderOwned) throw ApiException.NotFound("Folder", folderId.Value);
        }

        // ---------- 创建导图实体 ----------
        var now = DateTime.UtcNow;
        var map = new MindMapEntity
        {
            Id = Guid.NewGuid(),
            OwnerId = userId,
            FolderId = folderId,
            Title = mapTitle,
            Description = null,
            IsPublic = false,
            DefaultLayout = defaultLayout,
            Theme = theme,
            TemplateId = null,
            NodeCount = 0,
            CreatedAt = now,
            UpdatedAt = now,
            LastEditedAt = now
        };
        _db.MindMaps.Add(map);
        await _db.SaveChangesAsync(ct); // 先拿到 MindMapId

        // ---------- 递归创建节点 ----------
        var created = new List<Node>();
        var rootId = Guid.NewGuid();
        var rootNode = MaterializeNode(root, map.Id, null, rootId, isRoot: true, sortOrder: 0, isRootChild: false);
        created.Add(rootNode);

        var order = 0;
        foreach (var child in root.Children)
        {
            MaterializeRecursive(child, map.Id, rootId, isRootChild: true, sortOrder: order++, created);
        }

        map.RootNodeId = rootId;
        map.NodeCount = created.Count;

        _db.Nodes.AddRange(created);
        await _db.SaveChangesAsync(ct);

        // ---------- 返回详情 ----------
        return (await GetDetailAsync(userId, map.Id, ct))!;
    }

    // ============================================================
    //  中间数据结构
    // ============================================================
    private class ParsedNode
    {
        public string Title { get; set; } = "新节点";
        public string? Content { get; set; }
        public string? Note { get; set; }
        public bool IsCollapsed { get; set; }
        public Direction? Direction { get; set; }
        public string? Color { get; set; }
        public string? BackgroundColor { get; set; }
        public string? BorderColor { get; set; }
        public string? EdgeColor { get; set; }
        public int? FontSize { get; set; }
        public string? FontFamily { get; set; }
        public NodeShape? Shape { get; set; }
        public string? Icon { get; set; }
        public string? ExtraData { get; set; }
        public List<ParsedNode> Children { get; set; } = new();
    }

    // ============================================================
    //  实体化：ParsedNode -> EF Node，并递归子节点
    // ============================================================
    private static Node MaterializeNode(
        ParsedNode p, Guid mindMapId, Guid? parentId, Guid nodeId,
        bool isRoot, int sortOrder, bool isRootChild)
    {
        var title = string.IsNullOrWhiteSpace(p.Title) ? "新节点" : p.Title.Trim();
        if (title.Length > 512) title = title[..509] + "...";

        var node = new Node
        {
            Id = nodeId,
            MindMapId = mindMapId,
            ParentId = parentId,
            Title = title,
            Content = TrimToMax(p.Content, 8000),
            Note = TrimToMax(p.Note, 4000),
            SortOrder = sortOrder,
            IsCollapsed = p.IsCollapsed,
            Color = TrimToMax(p.Color, 32),
            FontSize = p.FontSize,
            FontFamily = TrimToMax(p.FontFamily, 64),
            Shape = p.Shape,
            Icon = TrimToMax(p.Icon, 64),
            BorderColor = TrimToMax(p.BorderColor, 32),
            Direction = isRootChild ? (p.Direction ?? Direction.Right) : null,
            BackgroundColor = TrimToMax(p.BackgroundColor, 32),
            EdgeColor = TrimToMax(p.EdgeColor, 32),
            EdgeStyle = null,
            ExtraData = TrimToMax(p.ExtraData, 8000),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        return node;
    }

    private static void MaterializeRecursive(
        ParsedNode p, Guid mindMapId, Guid parentId,
        bool isRootChild, int sortOrder, List<Node> acc)
    {
        var nodeId = Guid.NewGuid();
        var node = MaterializeNode(p, mindMapId, parentId, nodeId, isRoot: false, sortOrder, isRootChild);
        acc.Add(node);

        var order = 0;
        foreach (var c in p.Children)
        {
            MaterializeRecursive(c, mindMapId, nodeId, isRootChild: false, sortOrder: order++, acc);
        }
    }

    private static string? TrimToMax(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return s.Length > max ? s[..max] : s;
    }

    private static int CountNodes(ParsedNode n)
    {
        var c = 1;
        foreach (var ch in n.Children) c += CountNodes(ch);
        return c;
    }

    // ============================================================
    //  格式检测（无扩展名或扩展名不对时尝试所有解析器）
    // ============================================================
    private static ParsedNode? TryDetectAndParse(byte[] bytes)
    {
        // 尝试 JSON
        if (bytes.Length > 0 && bytes[0] == (byte)'{')
        {
            try { return ParseSimpleMindMapJson(bytes); } catch { /* ignore */ }
        }
        // 尝试 XML (FreeMind)
        if (bytes.Length > 0 && bytes[0] == (byte)'<')
        {
            try { return ParseFreeMind(bytes); } catch { /* ignore */ }
        }
        // 尝试 ZIP (XMind)
        if (bytes.Length >= 4 && bytes[0] == 0x50 && bytes[1] == 0x4B && bytes[2] == 0x03 && bytes[3] == 0x04)
        {
            try { return ParseXMind(bytes); } catch { /* ignore */ }
        }
        // 最后尝试 Markdown/纯文本
        try { return ParseMarkdown(bytes); } catch { return null; }
    }

    // ============================================================
    //  1. FreeMind (.mm) XML 解析
    // ============================================================
    private static ParsedNode? ParseFreeMind(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        var doc = XDocument.Load(ms);
        var mapEl = doc.Element("map");
        if (mapEl is null) throw ApiException.BadRequest("FreeMind 文件缺少 <map> 根元素");

        // 取第一个 node 作为根
        var firstNode = mapEl.Elements("node").FirstOrDefault();
        if (firstNode is null) return null;

        return BuildFreeMindNode(firstNode, isRoot: true, siblingIndex: 0);
    }

    private static ParsedNode BuildFreeMindNode(XElement el, bool isRoot, int siblingIndex)
    {
        var node = new ParsedNode
        {
            Title = el.Attribute("TEXT")?.Value ?? string.Empty
        };

        var pos = el.Attribute("POSITION")?.Value;
        if (!isRoot)
        {
            if (string.Equals(pos, "left", StringComparison.OrdinalIgnoreCase))
                node.Direction = Direction.Left;
            else if (string.Equals(pos, "right", StringComparison.OrdinalIgnoreCase))
                node.Direction = Direction.Right;
            else
                node.Direction = siblingIndex % 2 == 0 ? Direction.Right : Direction.Left;
        }

        var color = el.Attribute("COLOR")?.Value;
        if (IsValidColor(color)) node.Color = color;

        var bg = el.Attribute("BACKGROUND_COLOR")?.Value;
        if (IsValidColor(bg)) node.BackgroundColor = bg;

        var folded = el.Attribute("FOLDED")?.Value;
        if (string.Equals(folded, "true", StringComparison.OrdinalIgnoreCase))
            node.IsCollapsed = true;

        var children = el.Elements("node").ToList();
        for (var i = 0; i < children.Count; i++)
        {
            node.Children.Add(BuildFreeMindNode(children[i], isRoot: false, siblingIndex: i));
        }
        return node;
    }

    private static bool IsValidColor(string? c)
    {
        if (string.IsNullOrWhiteSpace(c)) return false;
        c = c.Trim();
        if (c.StartsWith('#'))
        {
            return c.Length is 4 or 7 or 9 && c.Skip(1).All(ch =>
                char.IsDigit(ch) || (ch >= 'a' && ch <= 'f') || (ch >= 'A' && ch <= 'F'));
        }
        return false;
    }

    // ============================================================
    //  2. simple-mind-map 原生 JSON 解析
    // ============================================================
    private static ParsedNode? ParseSimpleMindMapJson(byte[] bytes)
    {
        var json = Encoding.UTF8.GetString(bytes);
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (Exception ex) { throw ApiException.BadRequest($"JSON 解析失败：{ex.Message}"); }

        using (doc)
        {
            var root = doc.RootElement;
            // 兼容：整个文件就是节点对象，或根在 root / data 下
            JsonElement nodeEl = root;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("root", out var r) && r.ValueKind == JsonValueKind.Object) nodeEl = r;
            }
            if (nodeEl.ValueKind != JsonValueKind.Object)
                throw ApiException.BadRequest("JSON 结构不合法");

            var result = BuildSmmNode(nodeEl, isRoot: true);
            if (result is null || (string.IsNullOrWhiteSpace(result.Title) && result.Children.Count == 0))
                return null;
            if (string.IsNullOrWhiteSpace(result.Title)) result.Title = "中心主题";
            return result;
        }
    }

    private static ParsedNode? BuildSmmNode(JsonElement el, bool isRoot)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;

        var node = new ParsedNode();

        // 提取 data 块
        if (el.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            // 标题：richText 优先？不，text 是纯文本，富文本放在 Content
            if (data.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String)
                node.Title = textEl.GetString() ?? string.Empty;

            if (data.TryGetProperty("richText", out var rtEl) && rtEl.ValueKind == JsonValueKind.String)
                node.Content = rtEl.GetString();

            if (data.TryGetProperty("note", out var noteEl) && noteEl.ValueKind == JsonValueKind.String)
                node.Note = noteEl.GetString();

            if (data.TryGetProperty("color", out var colEl) && colEl.ValueKind == JsonValueKind.String)
                if (IsValidColor(colEl.GetString())) node.Color = colEl.GetString();

            if (data.TryGetProperty("fontSize", out var fsEl))
            {
                if (fsEl.ValueKind == JsonValueKind.Number && fsEl.TryGetInt32(out var fs)) node.FontSize = fs;
                else if (fsEl.ValueKind == JsonValueKind.String && int.TryParse(fsEl.GetString(), out var fs2)) node.FontSize = fs2;
            }

            if (data.TryGetProperty("fontFamily", out var ffEl) && ffEl.ValueKind == JsonValueKind.String)
                node.FontFamily = ffEl.GetString();

            if (data.TryGetProperty("fill", out var fillEl) && fillEl.ValueKind == JsonValueKind.String)
                if (IsValidColor(fillEl.GetString())) node.BackgroundColor = fillEl.GetString();

            if (data.TryGetProperty("strokeColor", out var scEl) && scEl.ValueKind == JsonValueKind.String)
                if (IsValidColor(scEl.GetString())) node.BorderColor = scEl.GetString();

            if (data.TryGetProperty("lineColor", out var lcEl) && lcEl.ValueKind == JsonValueKind.String)
                if (IsValidColor(lcEl.GetString())) node.EdgeColor = lcEl.GetString();

            if (data.TryGetProperty("icon", out var iconEl) && iconEl.ValueKind == JsonValueKind.String)
                node.Icon = iconEl.GetString();

            if (data.TryGetProperty("isExpand", out var ieEl))
            {
                var expanded = ieEl.ValueKind == JsonValueKind.True;
                // simple-mind-map 里 isExpand=true 表示展开，即 IsCollapsed=false
                node.IsCollapsed = !expanded;
            }

            // 方向：仅根直接子节点
            if (data.TryGetProperty("dir", out var dirEl) && dirEl.ValueKind == JsonValueKind.Number)
            {
                if (dirEl.TryGetInt32(out var dir))
                    node.Direction = dir == 0 ? Direction.Left : Direction.Right;
            }

            // 将 data 里其他未知字段放到 ExtraData（保留附件、链接等自定义扩展）
            try
            {
                var extra = new Dictionary<string, object?>();
                foreach (var prop in data.EnumerateObject())
                {
                    var keepKeys = new HashSet<string> { "text", "richText", "note", "color", "fontSize", "fontFamily", "fill", "strokeColor", "lineColor", "icon", "isExpand", "dir" };
                    if (!keepKeys.Contains(prop.Name))
                    {
                        extra[prop.Name] = JsonSerializer.Deserialize<object>(prop.Value.GetRawText());
                    }
                }
                if (extra.Count > 0)
                    node.ExtraData = JsonSerializer.Serialize(extra);
            }
            catch { /* 忽略 */ }
        }
        else
        {
            // 兼容直接在根对象放 text 的情况
            if (el.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String)
                node.Title = textEl.GetString() ?? string.Empty;
        }

        // 子节点
        if (el.TryGetProperty("children", out var childrenEl) && childrenEl.ValueKind == JsonValueKind.Array)
        {
            var i = 0;
            foreach (var c in childrenEl.EnumerateArray())
            {
                var child = BuildSmmNode(c, isRoot: false);
                if (child is not null)
                {
                    // 设置默认方向（根直接子节点）
                    if (isRoot && child.Direction is null)
                        child.Direction = i % 2 == 0 ? Direction.Right : Direction.Left;
                    node.Children.Add(child);
                    i++;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(node.Title) && node.Children.Count == 0) return null;
        return node;
    }

    // ============================================================
    //  3. Markdown 大纲解析
    // ============================================================
    private static ParsedNode? ParseMarkdown(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes);
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

        // 栈：每个层级当前最后一个 ParsedNode。栈深度对应 Markdown 层级。
        var stack = new List<ParsedNode>();
        ParsedNode? root = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            if (string.IsNullOrWhiteSpace(line)) continue;

            (int depth, string content) = ParseMarkdownLine(line);
            if (depth < 0 || string.IsNullOrWhiteSpace(content)) continue;

            var node = new ParsedNode { Title = content.Trim() };

            if (depth == 0)
            {
                // 根节点：只取第一个（后续根级标题作为根的子节点）
                if (root is null)
                {
                    root = node;
                    stack.Clear();
                    stack.Add(root);
                    continue;
                }
                // 第二个 # 标题：当作根的一级子节点
                depth = 1;
            }

            // 保证栈至少有 root
            if (stack.Count == 0)
            {
                root ??= new ParsedNode { Title = "中心主题" };
                stack.Add(root);
            }

            // 栈调整：找到父节点（stack[depth-1]）
            var targetParentDepth = depth - 1;
            if (targetParentDepth >= stack.Count)
            {
                // 深度跳变太大，挂到最后一个
                targetParentDepth = stack.Count - 1;
            }
            var parent = stack[targetParentDepth];
            parent.Children.Add(node);

            // 更新栈：depth 索引位置 = 当前 node
            if (stack.Count > depth)
            {
                stack[depth] = node;
                stack.RemoveRange(depth + 1, stack.Count - depth - 1);
            }
            else
            {
                stack.Add(node);
            }
        }

        if (root is null) return null;
        if (string.IsNullOrWhiteSpace(root.Title)) root.Title = "中心主题";
        return root;
    }

    /// <summary>解析一行，返回 (depth, content)。depth=-1 表示非大纲行。</summary>
    private static (int depth, string content) ParseMarkdownLine(string line)
    {
        var trimmed = line.TrimStart();
        // ATX 标题：### Title
        if (trimmed.StartsWith('#'))
        {
            var hashes = 0;
            while (hashes < trimmed.Length && trimmed[hashes] == '#') hashes++;
            if (hashes <= trimmed.Length && (hashes == trimmed.Length || trimmed[hashes] == ' ' || trimmed[hashes] == '\t'))
            {
                var content = hashes >= trimmed.Length ? string.Empty : trimmed[(hashes + 1)..];
                return (hashes - 1, content.Trim()); // # -> depth 0 (根)
            }
        }

        // 缩进 + 列表项
        var leadingSpaces = line.Length - line.TrimStart().Length;
        var indentDepth = leadingSpaces / 2; // 每 2 空格一个层级
        var t = line.TrimStart();

        var listPrefixMatch = false;
        if (t.StartsWith("- ") || t.StartsWith("* ") || t.StartsWith("+ "))
        {
            t = t[2..];
            listPrefixMatch = true;
        }
        else
        {
            // 数字 + 点，例如 "1. item"
            var dotIdx = t.IndexOf(". ", StringComparison.Ordinal);
            if (dotIdx > 0 && t[..dotIdx].All(char.IsDigit))
            {
                t = t[(dotIdx + 2)..];
                listPrefixMatch = true;
            }
        }

        if (listPrefixMatch)
        {
            // Markdown 列表项挂到根下的第 1 层起（depth = indentDepth + 1，根独占 depth 0）
            return (indentDepth + 1, t);
        }

        // 普通非空行：没有前缀时也当作根的第 1 层节点（一段一行）
        return (1, trimmed);
    }

    // ============================================================
    //  4. XMind (.xmind) 解析（ZIP -> content.json）
    // ============================================================
    private static ParsedNode? ParseXMind(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: false);

        // XMind 2020+：content.json；XMind 8：content.xml
        var entryJson = zip.Entries.FirstOrDefault(e =>
            string.Equals(e.Name, "content.json", StringComparison.OrdinalIgnoreCase));
        if (entryJson is not null)
        {
            using var es = entryJson.Open();
            using var rdr = new StreamReader(es, Encoding.UTF8);
            var json = rdr.ReadToEnd();
            return ParseXMind2020Json(json);
        }

        var entryXml = zip.Entries.FirstOrDefault(e =>
            string.Equals(e.Name, "content.xml", StringComparison.OrdinalIgnoreCase) ||
            e.FullName.Contains("content.xml"));
        if (entryXml is not null)
        {
            using var es = entryXml.Open();
            var doc = XDocument.Load(es);
            return ParseXMind8Xml(doc);
        }

        throw ApiException.BadRequest("XMind 文件未找到 content.json 或 content.xml");
    }

    private static ParsedNode? ParseXMind2020Json(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (Exception ex) { throw ApiException.BadRequest($"XMind content.json 解析失败：{ex.Message}"); }

        using (doc)
        {
            // 顶层是数组 [ { rootTopic: {...} } ]
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            foreach (var sheet in doc.RootElement.EnumerateArray())
            {
                if (sheet.ValueKind != JsonValueKind.Object) continue;
                if (!sheet.TryGetProperty("rootTopic", out var rootTopic) || rootTopic.ValueKind != JsonValueKind.Object) continue;
                var result = BuildXmindJsonNode(rootTopic, isRoot: true);
                if (result is not null) return result;
            }
        }
        return null;
    }

    private static ParsedNode? BuildXmindJsonNode(JsonElement el, bool isRoot)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        var node = new ParsedNode();

        if (el.TryGetProperty("title", out var tEl) && tEl.ValueKind == JsonValueKind.String)
            node.Title = tEl.GetString() ?? string.Empty;

        if (el.TryGetProperty("notes", out var notesEl) && notesEl.ValueKind == JsonValueKind.Object)
        {
            if (notesEl.TryGetProperty("plain", out var plainEl) && plainEl.ValueKind == JsonValueKind.Object)
            {
                if (plainEl.TryGetProperty("content", out var cntEl) && cntEl.ValueKind == JsonValueKind.String)
                    node.Note = cntEl.GetString();
            }
        }

        // 样式
        if (el.TryGetProperty("style", out var styleEl) && styleEl.ValueKind == JsonValueKind.Object)
        {
            if (styleEl.TryGetProperty("properties", out var propsEl) && propsEl.ValueKind == JsonValueKind.Object)
            {
                if (propsEl.TryGetProperty("fo:color", out var cEl) && cEl.ValueKind == JsonValueKind.String)
                    if (IsValidColor(cEl.GetString())) node.Color = cEl.GetString();
                if (propsEl.TryGetProperty("svg:fill", out var fEl) && fEl.ValueKind == JsonValueKind.String)
                    if (IsValidColor(fEl.GetString())) node.BackgroundColor = fEl.GetString();
                if (propsEl.TryGetProperty("fo:font-size", out var fsEl) && fsEl.ValueKind == JsonValueKind.String)
                {
                    var raw = fsEl.GetString() ?? string.Empty;
                    var num = new string(raw.TakeWhile(char.IsDigit).ToArray());
                    if (int.TryParse(num, out var v)) node.FontSize = v;
                }
            }
        }

        // 子节点：attached（两边）、detached（游离）我们取 attached
        if (el.TryGetProperty("children", out var childrenEl) && childrenEl.ValueKind == JsonValueKind.Object)
        {
            var order = 0;
            if (childrenEl.TryGetProperty("attached", out var attached) && attached.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in attached.EnumerateArray())
                {
                    var child = BuildXmindJsonNode(c, isRoot: false);
                    if (child is not null)
                    {
                        if (isRoot && child.Direction is null)
                            child.Direction = order % 2 == 0 ? Direction.Right : Direction.Left;
                        node.Children.Add(child);
                        order++;
                    }
                }
            }
        }

        if (string.IsNullOrWhiteSpace(node.Title) && node.Children.Count == 0) return null;
        return node;
    }

    private static ParsedNode? ParseXMind8Xml(XDocument doc)
    {
        // XMind 8: <sheet><topic>...</topic></sheet>
        var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;
        var sheet = doc.Descendants(ns + "sheet").FirstOrDefault();
        var rootTopic = sheet?.Element(ns + "topic");
        if (rootTopic is null) return null;
        return BuildXmind8XmlNode(rootTopic, ns, isRoot: true);
    }

    private static ParsedNode? BuildXmind8XmlNode(XElement el, XNamespace ns, bool isRoot)
    {
        var node = new ParsedNode();
        var titleEl = el.Element(ns + "title");
        if (titleEl is not null) node.Title = titleEl.Value ?? string.Empty;

        var notesEl = el.Element(ns + "notes");
        if (notesEl is not null) node.Note = notesEl.Value;

        // 子节点
        var childrenEl = el.Element(ns + "children");
        if (childrenEl is not null)
        {
            var topicsEl = childrenEl.Element(ns + "topics");
            if (topicsEl is not null)
            {
                var order = 0;
                foreach (var t in topicsEl.Elements(ns + "topic"))
                {
                    var child = BuildXmind8XmlNode(t, ns, isRoot: false);
                    if (child is not null)
                    {
                        if (isRoot && child.Direction is null)
                            child.Direction = order % 2 == 0 ? Direction.Right : Direction.Left;
                        node.Children.Add(child);
                        order++;
                    }
                }
            }
        }

        if (string.IsNullOrWhiteSpace(node.Title) && node.Children.Count == 0) return null;
        return node;
    }

    // ============================================================
    //  辅助：获取导图详情（与 MindMapService.GetAsync 类似的投影）
    // ============================================================
    private async Task<MindMapDetailDto?> GetDetailAsync(Guid? userId, Guid id, CancellationToken ct)
    {
        var dto = await _db.MindMaps
            .Where(m => m.Id == id && (
                (userId.HasValue && m.OwnerId == userId.Value) ||
                (m.IsPublic && !m.IsTakenDown)))
            .Select(m => new MindMapDetailDto
            {
                Id = m.Id,
                Title = m.Title,
                Description = m.Description,
                IsPublic = m.IsPublic,
                CoverImage = m.CoverImage,
                DefaultLayout = (int)m.DefaultLayout,
                NodeCount = m.NodeCount,
                CreatedAt = m.CreatedAt,
                UpdatedAt = m.UpdatedAt,
                LastEditedAt = m.LastEditedAt,
                Theme = m.Theme,
                TemplateId = m.TemplateId,
                RootNodeId = m.RootNodeId,
                OwnerId = m.OwnerId,
                OwnerName = m.Owner.Username,
                FolderId = m.FolderId,
                FolderName = m.Folder != null ? m.Folder.Name : null,
                Tags = m.Tags.Select(t => new TagBriefDto { Id = t.Id, Name = t.Name, Color = t.Color }).ToList()
            })
            .FirstOrDefaultAsync(ct);
        return dto;
    }
}
