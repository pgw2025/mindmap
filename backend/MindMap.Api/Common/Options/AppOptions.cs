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
}
