using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MindMap.Api.Common.Options;
using MindMap.Api.Infrastructure.Data;

namespace MindMap.Api.Application.Services;

/// <summary>
/// 节点图片孤儿文件清理后台服务。
/// 节点图片存储在 {UploadRoot}/nodes/**，元数据挂在 Node.ExtraData（JSON）的 image 字段。
/// 删除节点/替换图片后旧文件无人引用（孤儿）。本服务定期扫描文件目录，
/// 与全库节点引用比对，删除「未被引用且超过最小文件年龄」的文件。
/// 最小文件年龄保护：用户刚上传、尚未保存到节点的文件不会被误删。
/// </summary>
public class NodeImageCleanupService : BackgroundService
{
    private static readonly Regex UploadUrlPattern = new("\"/uploads/nodes/[^\"]+\"", RegexOptions.Compiled);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AppOptions _options;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<NodeImageCleanupService> _logger;

    public NodeImageCleanupService(
        IServiceScopeFactory scopeFactory,
        IOptions<AppOptions> options,
        IWebHostEnvironment env,
        ILogger<NodeImageCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _env = env;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 避开启动高峰（迁移/种子还在跑）
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var intervalHours = _options.ImageCleanupIntervalHours >= 1 ? _options.ImageCleanupIntervalHours : 24;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "节点图片清理执行失败，将于下个周期重试");
            }

            try
            {
                await Task.Delay(TimeSpan.FromHours(intervalHours), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task CleanupOnceAsync(CancellationToken ct)
    {
        var uploadRoot = ResolveUploadRoot();
        var nodesDir = Path.Combine(uploadRoot, "nodes");
        if (!Directory.Exists(nodesDir)) return;

        // 1. 收集全库被引用的上传 URL
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var extraDataList = await db.Nodes
            .Where(n => n.ExtraData != null && n.ExtraData.Contains("/uploads/nodes/"))
            .Select(n => n.ExtraData!)
            .ToListAsync(ct);

        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var extraData in extraDataList)
        {
            foreach (Match match in UploadUrlPattern.Matches(extraData))
            {
                // 去掉引号："/uploads/nodes/202610/xxx.png" → /uploads/nodes/202610/xxx.png
                referenced.Add(match.Value.Trim('"'));
            }
        }

        // 2. 遍历文件目录，删除「未被引用 + 超过最小年龄」的文件
        var minAge = TimeSpan.FromHours(
            _options.ImageCleanupMinFileAgeHours >= 1 ? _options.ImageCleanupMinFileAgeHours : 24);
        var now = DateTime.UtcNow;

        var files = Directory.EnumerateFiles(nodesDir, "*", SearchOption.AllDirectories);
        var deleted = 0;
        var total = 0;
        foreach (var file in files)
        {
            total++;
            var relPath = $"/uploads/{Path.GetRelativePath(uploadRoot, file).Replace('\\', '/')}";
            if (referenced.Contains(relPath)) continue;

            try
            {
                var age = now - System.IO.File.GetLastWriteTimeUtc(file);
                if (age < minAge) continue;
                System.IO.File.Delete(file);
                deleted++;
                _logger.LogInformation("节点图片清理：删除孤儿文件 {RelPath}", relPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "节点图片清理：文件 {RelPath} 删除失败，跳过", relPath);
            }
        }

        // 3. 清理空目录（按月分桶的目录删除后遗留空壳）
        CleanEmptyDirs(nodesDir);

        if (total > 0)
            _logger.LogInformation("节点图片清理完成：共扫描 {Total} 个文件，删除 {Deleted} 个", total, deleted);
    }

    private string ResolveUploadRoot()
    {
        var root = _options.UploadRoot;
        return Path.IsPathRooted(root) ? root : Path.Combine(_env.ContentRootPath, root);
    }

    private static void CleanEmptyDirs(string root)
    {
        // 只清理 nodes 下的空目录（先深后浅）
        foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                     .OrderByDescending(d => d.Length))
        {
            try
            {
                if (Directory.EnumerateFileSystemEntries(dir).Any()) continue;
                Directory.Delete(dir);
            }
            catch
            {
                // 目录清理失败不阻塞主流程
            }
        }
    }
}
