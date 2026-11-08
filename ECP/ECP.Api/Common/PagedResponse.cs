namespace ECP.Api.Dtos.Common;

/// <summary>
/// 通用分页响应，保存分页信息和当前页数据
/// </summary>
/// <typeparam name="T">当前页每条数据的类型</typeparam>
public class PagedResponse<T>
{
    // 实际返回的页码，从 1 开始
    public int PageNumber { get; set; }

    // 每页最多返回的数据条数
    public int PageSize { get; set; }

    // 满足筛选条件的数据总数
    public int TotalCount { get; set; }

    // 总页数，没有数据时保留第 1 页
    public int TotalPages { get; set; }

    // 创建当前页数据列表，列表元素类型由调用方指定
    public List<T> Items { get; set; } = new List<T>();
}