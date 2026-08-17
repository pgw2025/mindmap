using System.ComponentModel.DataAnnotations;

namespace MindMap.Api.Application.DTOs.Auth;

public class RegisterRequest
{
    [Required, StringLength(32, MinimumLength = 3)]
    public string Username { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(128)]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(64, MinimumLength = 8)]
    public string Password { get; set; } = string.Empty;
}
