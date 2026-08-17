namespace MindMap.Api.Domain.Entities.Enums;

/// <summary>
/// 节点形状。
/// </summary>
public enum NodeShape
{
    /// <summary>矩形</summary>
    Rectangle = 0,

    /// <summary>圆角矩形</summary>
    Rounded = 1,

    /// <summary>圆形</summary>
    Circle = 2,

    /// <summary>椭圆</summary>
    Ellipse = 3,

    /// <summary>菱形</summary>
    Diamond = 4,

    /// <summary>平行四边形</summary>
    Parallelogram = 5,

    /// <summary>下划线（仅显示文字）</summary>
    Underline = 6
}
