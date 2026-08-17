using System.ComponentModel.DataAnnotations;

namespace MindMap.Api.Application.DTOs.Auth;

public class LoginRequest
{
    /// <summary>用户名或邮箱</summary>
    [Required, StringLength(128)]
    public string Account { get; set; } = string.Empty;

    [Required, StringLength(64)]
    public string Password { get; set; } = string.Empty;
}
