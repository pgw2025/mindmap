namespace MindMap.Api.Common.Extensions;

public static class HttpContextExtensions
{
    /// <summary>获取客户端 IP，优先取 X-Forwarded-For 首段（反向代理场景）。</summary>
    public static string? GetClientIp(this HttpContext ctx)
    {
        var forwarded = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            return forwarded.Split(',')[0].Trim();
        }
        return ctx.Connection.RemoteIpAddress?.ToString();
    }
}
