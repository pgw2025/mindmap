namespace MindMap.Api.Common.Responses;

/// <summary>
/// 分页响应包装。
/// </summary>
public class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; set; } = Array.Empty<T>();
    public long Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public long TotalPages => PageSize <= 0 ? 0 : (long)Math.Ceiling((double)Total / PageSize);

    public static PagedResult<T> Create(IReadOnlyList<T> items, long total, int page, int pageSize)
        => new() { Items = items, Total = total, Page = page, PageSize = pageSize };
}
