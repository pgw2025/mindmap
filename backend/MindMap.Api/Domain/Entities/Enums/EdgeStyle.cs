namespace MindMap.Api.Domain.Entities.Enums;

/// <summary>
/// 节点之间的连线样式。
/// </summary>
public enum EdgeStyle
{
    /// <summary>实线</summary>
    Solid = 0,

    /// <summary>虚线</summary>
    Dashed = 1,

    /// <summary>点线</summary>
    Dotted = 2,

    /// <summary>贝塞尔曲线</summary>
    Curve = 3
}
