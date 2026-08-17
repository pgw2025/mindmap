namespace MindMap.Api.Domain.Entities.Enums;

/// <summary>
/// 思维导图布局方向。
/// </summary>
public enum MindMapLayout
{
    /// <summary>横向：根在左</summary>
    Left = 0,

    /// <summary>横向：根在右</summary>
    Right = 1,

    /// <summary>纵向：根在上</summary>
    Top = 2,

    /// <summary>纵向：根在下</summary>
    Bottom = 3,

    /// <summary>放射状</summary>
    Radial = 4
}
