using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MindMap.Api.Common.Options;
using MindMap.Api.Infrastructure.Data;

namespace MindMap.Api.Application.Services;

/// <summary>
/// 回收站过期清理后台服务。
/// 每隔 CleanupIntervalHours 扫描一次软删导图，按「该用户生效的保留天数」
/// 计算过期并物理删除（复用 IMindMapService.HardDeleteAsync）。
/// 单条删除失败只记日志不影响后续——回收站本身是容错场景。
/// </summary>
public class RecycleBinCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RecycleBinOptions _options;
    private readonly ILogger<RecycleBinCleanupService> _logger;

    public RecycleBinCleanupService(IServiceScopeFactory scopeFactory, IOptions<RecycleBinOptions> options, ILogger<RecycleBinCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 启动后先等一小段，避开启动高峰（迁移/种子还在跑）。
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var intervalHours = _options.CleanupIntervalHours >= 1 ? _options.CleanupIntervalHours : 6;
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
                _logger.LogWarning(ex, "回收站清理执行失败，将于下个周期重试");
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
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var mindMaps = scope.ServiceProvider.GetRequiredService<IMindMapService>();

        var now = DateTime.UtcNow;

        // 软删导图 + 所属用户的个人保留天数，按用户分组解算过期时间。
        var recycled = await db.MindMaps
            .IgnoreQueryFilters()
            .Where(m => m.DeletedAt != null)
            .Select(m => new { m.Id, m.OwnerId, m.DeletedAt, m.Owner.RecycleBinRetentionDays })
            .ToListAsync(ct);

        if (recycled.Count == 0) return;

        var expired = recycled
            .Where(x =>
            {
                var days = RecycleBinRetention.GetEffectiveDays(x.RecycleBinRetentionDays, _options.DefaultRetentionDays);
                return x.DeletedAt!.Value.AddDays(days) <= now;
            })
            .Select(x => x.Id)
            .ToList();

        if (expired.Count == 0) return;

        _logger.LogInformation("回收站清理开始：软删共 {Total} 个，过期 {Expired} 个", recycled.Count, expired.Count);

        var deleted = 0;
        foreach (var id in expired)
        {
            try
            {
                await mindMaps.HardDeleteAsync(id, ct);
                deleted++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "回收站导图 {Id} 清理失败，跳过", id);
            }
        }

        _logger.LogInformation("回收站清理完成：本次物理删除 {Deleted} 个", deleted);
    }
}
