using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using MindMap.Api.Common.Exceptions;
using MindMap.Api.Common.Responses;

namespace MindMap.Api.Common.Filters;

/// <summary>
/// 全局异常过滤器。统一将异常包装为 ApiResult 返回，避免泄露内部堆栈。
/// </summary>
public class GlobalExceptionFilter : IExceptionFilter
{
    private readonly ILogger<GlobalExceptionFilter> _logger;

    public GlobalExceptionFilter(ILogger<GlobalExceptionFilter> logger)
    {
        _logger = logger;
    }

    public void OnException(ExceptionContext context)
    {
        var ex = context.Exception;
        var statusCode = ex is ApiException apiEx
            ? apiEx.StatusCode
            : StatusCodes.Status500InternalServerError;

        _logger.LogError(ex, "Unhandled exception: {Message}", ex.Message);

        context.Result = new ObjectResult(ApiResult.Fail(ex.Message, statusCode))
        {
            StatusCode = statusCode
        };
        context.ExceptionHandled = true;
    }
}
