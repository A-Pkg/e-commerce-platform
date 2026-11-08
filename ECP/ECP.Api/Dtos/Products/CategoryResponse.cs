namespace ECP.Api.Dtos.Products;

/// <summary>
/// 接口返回的分类信息
/// </summary>
public class CategoryResponse
{
    // 分类编号
    public int Id { get; set; }

    // 父分类编号，null 表示大分类，有值表示所属大分类的编号
    public int? ParentId { get; set; }

    // 分类名称
    public string Name { get; set; } = string.Empty;

    // 展示顺序
    public int SortOrder { get; set; }

    // 是否启用
    public bool IsActive { get; set; }

    // 创建时间，使用 UTC
    public DateTime CreatedAtUtc { get; set; }
}