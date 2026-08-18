using MindMap.Api.Application.DTOs.Admin;
using MindMap.Api.Common.Responses;

namespace MindMap.Api.Application.Services;

public interface IAdminService
{
    // ---------- 用户 ----------
    Task<PagedResult<AdminUserListItemDto>> GetUsersAsync(AdminUserListQuery query, CancellationToken ct = default);
    Task UpdateUserAsync(Guid targetUserId, AdminUserUpdateRequest req, Guid operatorUserId, CancellationToken ct = default);
    Task DeleteUserAsync(Guid targetUserId, Guid operatorUserId, CancellationToken ct = default);

    // ---------- 导图 ----------
    Task<PagedResult<AdminMindMapListItemDto>> GetMindMapsAsync(AdminMindMapListQuery query, CancellationToken ct = default);
    Task TakeDownMindMapAsync(Guid mindMapId, AdminMindMapTakeDownRequest req, Guid operatorUserId, CancellationToken ct = default);
    Task RestoreMindMapAsync(Guid mindMapId, Guid operatorUserId, CancellationToken ct = default);
    Task DeleteMindMapAsync(Guid mindMapId, Guid operatorUserId, CancellationToken ct = default);

    // ---------- 统计 ----------
    Task<AdminStatsDto> GetStatsAsync(CancellationToken ct = default);

    // ---------- 举报 ----------
    Task<PagedResult<AdminReportListItemDto>> GetReportsAsync(AdminReportListQuery query, CancellationToken ct = default);
    Task<AdminReportListItemDto> CreateReportAsync(Guid? reporterId, Guid mindMapId, AdminReportCreateRequest req, CancellationToken ct = default);
    Task ResolveReportAsync(Guid reportId, AdminReportResolveRequest req, Guid operatorUserId, CancellationToken ct = default);
}
