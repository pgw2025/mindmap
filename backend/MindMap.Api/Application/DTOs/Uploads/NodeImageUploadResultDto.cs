namespace MindMap.Api.Application.DTOs.Uploads;

/// <summary>节点图片上传结果。url 为站内相对路径，可直接用于渲染与导出。</summary>
public class NodeImageUploadResultDto
{
    public string Url { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
}
