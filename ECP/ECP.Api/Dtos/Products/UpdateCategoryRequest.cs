using System.ComponentModel.DataAnnotations;

namespace ECP.Api.Dtos.Products;

/// <summary>
/// 管理员修改分类的请求数据
/// </summary>
public class UpdateCategoryRequest
{
    // 父分类编号，null 表示设为大分类，有值时必须为正整数
    [Range(1, int.MaxValue, ErrorMessage = "父分类编号必须大于 0")]
    public int? ParentId { get; set; }

    // 分类名称
    [Required(ErrorMessage = "请输入分类名称")]
    [StringLength(100, ErrorMessage = "分类名称不能超过 100 个字符")]
    public string Name { get; set; } = string.Empty;

    // 展示顺序，数值越小越靠前
    [Range(0, int.MaxValue, ErrorMessage = "排序值不能小于 0")]
    public int SortOrder { get; set; }

    // 是否启用
    public bool IsActive { get; set; } = true;
}