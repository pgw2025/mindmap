using System.ComponentModel.DataAnnotations;

namespace MindMap.Api.Application.DTOs.Auth;

public class RefreshRequest
{
    [Required]
    public string RefreshToken { get; set; } = string.Empty;
}
