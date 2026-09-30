namespace MindMap.Api.Common.Options;

/// <summary>
/// 回收站配置选项（appsettings.json 中 "RecycleBin" 节点）。
/// </summary>
public class RecycleBinOptions
{
    /// <summary>全局默认保留天数。用户未设置个人覆盖时使用。</summary>
    public int DefaultRetentionDays { get; set; } = 30;

    /// <summary>过期清理服务的执行间隔（小时）。</summary>
    public int CleanupIntervalHours { get; set; } = 6;
}
