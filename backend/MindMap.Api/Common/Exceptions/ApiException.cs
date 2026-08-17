namespace MindMap.Api.Common.Exceptions;

/// <summary>
/// 业务异常。在 Service 层抛出，由全局异常过滤器拦截并转换成 400 响应。
/// </summary>
public class ApiException : Exception
{
    public int StatusCode { get; }

    public ApiException(string message, int statusCode = StatusCodes.Status400BadRequest)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public static ApiException NotFound(string entity, object key)
        => new($"{entity} 不存在 (key={key})", StatusCodes.Status404NotFound);

    public static ApiException Forbidden(string message = "无访问权限")
        => new(message, StatusCodes.Status403Forbidden);

    public static ApiException Conflict(string message)
        => new(message, StatusCodes.Status409Conflict);
}
