namespace MindMap.Api.Common.Options;

/// <summary>
/// JWT 配置选项（appsettings.json 中 "Jwt" 节点）。
/// </summary>
public class JwtOptions
{
    public string Issuer { get; set; } = "MindMap";
    public string Audience { get; set; } = "MindMap";
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 120;
    public int RefreshTokenDays { get; set; } = 14;
}
