namespace MindMap.Api.Common.Options;

/// <summary>
/// 应用级配置选项（appsettings.json 中 "App" 节点）。
/// </summary>
public class AppOptions
{
    public string Name { get; set; } = "MindMap";
    public string[] CorsOrigins { get; set; } = Array.Empty<string>();
    public string UploadRoot { get; set; } = "wwwroot/uploads";
    public string WebPublicBase { get; set; } = "http://localhost:5000";
    /// <summary>节点图片孤儿文件清理周期（小时）。</summary>
    public int ImageCleanupIntervalHours { get; set; } = 24;
    /// <summary>孤儿文件判定最小文件年龄（小时）：小于该年龄的最近上传文件不删，避免误删未落库的上传。</summary>
    public int ImageCleanupMinFileAgeHours { get; set; } = 24;
}
