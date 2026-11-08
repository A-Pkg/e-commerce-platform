using System.ComponentModel.DataAnnotations;

namespace ECP.Api.Dtos.Products;

/// <summary>
/// 管理员编辑 SKU 的请求数据，不允许修改规格组合
/// </summary>
public class UpdateProductSkuRequest
{
    // 修改后的售价
    [Required(ErrorMessage = "请输入售价")]
    [Range(
        typeof(decimal),
        "0",
        "9999999999999999.99",
        ErrorMessage = "售价超出允许范围")]
    public decimal? Price { get; set; }

    // 修改后的库存
    [Required(ErrorMessage = "请输入库存")]
    [Range(0, int.MaxValue, ErrorMessage = "库存必须为非负整数")]
    public int? Stock { get; set; }

    // 修改后的启用状态
    [Required(ErrorMessage = "请明确指定是否启用")]
    public bool? IsActive { get; set; }

    // 打开编辑表单时的库存，用于检查保存前库存是否已经变化
    [Required(ErrorMessage = "缺少原库存，请重新打开编辑")]
    [Range(0, int.MaxValue, ErrorMessage = "原库存无效")]
    public int? ExpectedStock { get; set; }

    // 前端为一次保存生成的请求标识，同次重试复用
    [Required(ErrorMessage = "缺少保存请求标识")]
    [RegularExpression(
        "^[a-fA-F0-9]{32}$",
        ErrorMessage = "保存请求标识必须为 32 位十六进制字符")]
    public string RequestId { get; set; } = string.Empty;
}