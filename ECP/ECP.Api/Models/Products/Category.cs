namespace ECP.Api.Models.Products;

/// <summary>
/// 商品分类实体
/// </summary>
public class Category
{
    // 分类编号
    public int Id { get; set; }

    // 父分类编号，null 表示大分类，有值表示所属大分类的编号
    public int? ParentId { get; set; }

    // 分类名称
    public string Name { get; set; } = string.Empty;

    // 展示顺序，数值越小越靠前，默认是 0
    public int SortOrder { get; set; }

    // 是否启用，新分类默认启用
    public bool IsActive { get; set; } = true;

    // 分类创建时间，统一使用 UTC
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}