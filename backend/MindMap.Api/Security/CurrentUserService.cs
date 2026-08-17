using System.Security.Claims;

namespace MindMap.Api.Security;

public interface ICurrentUserService
{
    Guid? UserId { get; }
    string? Username { get; }
    bool IsAdmin { get; }
    bool IsAuthenticated { get; }
}

/// <summary>
/// 从 HttpContext 的 ClaimsPrincipal 中解析当前登录用户。
/// </summary>
public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _accessor;

    public CurrentUserService(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    private ClaimsPrincipal? User => _accessor.HttpContext?.User;

    public Guid? UserId
    {
        get
        {
            var id = User?.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(id, out var g) ? g : null;
        }
    }

    public string? Username => User?.FindFirstValue(ClaimTypes.Name);

    public bool IsAdmin =>
        User?.IsInRole("admin") == true;

    public bool IsAuthenticated =>
        User?.Identity?.IsAuthenticated == true;
}
