namespace ECP.Api.Models.Stocks;

/// <summary>
/// 库存流水实体，保存一次库存变化及变动后的可售库存
/// </summary>
public class StockMovement
{
    // 库存流水内部编号
    public int Id { get; set; }

    // 库存业务流水号，由后续业务按库存事件生成，同一事件重试时复用
    public string MovementNo { get; set; } = string.Empty;

    // 本次库存变化所属的 SKU 编号
    public int SkuId { get; set; }

    // 库存变化数量，正数表示增加，负数表示减少，不允许为零
    public int QuantityChange { get; set; }

    // 完成本次库存变化后的可售库存，不能为负数
    public int BalanceAfter { get; set; }

    // 库存变化原因，由业务使用 StockMovementReasons 中的常量明确填写
    public string Reason { get; set; } = string.Empty;

    // 关联订单编号，订单引起的变化填写，其他变化可为空
    public int? OrderId { get; set; }

    // 操作用户编号，人工操作取当前登录用户，系统自动处理可为空
    public int? OperatorUserId { get; set; }

    // 库存变化发生时间，统一使用 UTC
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}