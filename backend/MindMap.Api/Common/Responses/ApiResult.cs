using System.Net;

namespace MindMap.Api.Common.Responses;

/// <summary>
/// 统一 API 响应包装器。所有控制器统一返回此结构，前端按 code 字段判断成功/失败。
/// </summary>
public class ApiResult
{
    public int Code { get; set; }
    public bool Success => Code == 0;
    public string? Message { get; set; }
    public object? Data { get; set; }

    public static ApiResult Ok(object? data = null, string? message = null)
        => new() { Code = 0, Data = data, Message = message };

    public static ApiResult Fail(string message, HttpStatusCode code = HttpStatusCode.BadRequest)
        => new() { Code = (int)code, Message = message };

    public static ApiResult Fail(string message, int code)
        => new() { Code = code, Message = message };
}

public class ApiResult<T> : ApiResult
{
    public new T? Data { get; set; }

    public static ApiResult<T> Ok(T? data, string? message = null)
        => new() { Code = 0, Data = data, Message = message };
}
