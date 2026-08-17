using MindMap.Api.Application.DTOs.Auth;

namespace MindMap.Api.Application.Services;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request, string? ip, CancellationToken ct = default);
    Task<AuthResponse> LoginAsync(LoginRequest request, string? ip, CancellationToken ct = default);
    Task<AuthResponse> RefreshAsync(string refreshToken, string? ip, CancellationToken ct = default);
    Task LogoutAsync(string refreshToken, CancellationToken ct = default);
    Task<UserDto?> GetCurrentUserAsync(Guid userId, CancellationToken ct = default);
}
