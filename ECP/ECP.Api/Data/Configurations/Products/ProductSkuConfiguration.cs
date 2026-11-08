using ECP.Api.Models.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECP.Api.Data.Configurations.Products;

/// <summary>
/// 商品 SKU 的数据库映射配置
/// </summary>
public class ProductSkuConfiguration : IEntityTypeConfiguration<ProductSku>
{
    // 配置 SKU 表的字段、商品关联、检查约束和索引
    public void Configure(EntityTypeBuilder<ProductSku> builder)
    {
        // 配置数据库表名，以及价格和库存的非负约束
        builder.ToTable("product_skus", tableBuilder =>
        {
            // 数据库禁止保存负数价格
            tableBuilder.HasCheckConstraint(
                "CK_product_skus_price_nonnegative",
                "`Price` >= 0");

            // 数据库禁止保存负数库存
            tableBuilder.HasCheckConstraint(
                "CK_product_skus_stock_nonnegative",
                "`Stock` >= 0");
        });

        // SKU 内部编号作为主键
        builder.HasKey(sku => sku.Id);

        // SKU 内部编号由数据库自动生成
        builder.Property(sku => sku.Id)
            .ValueGeneratedOnAdd();

        // 每个 SKU 必须有所属商品
        builder.Property(sku => sku.ProductId)
            .IsRequired();

        // SKU 业务编码必填，最多 64 个字符
        builder.Property(sku => sku.SkuCode)
            .HasMaxLength(64)
            .IsRequired();

        // 规格组合摘要必填，最多 64 个字符
        builder.Property(sku => sku.SpecificationKey)
            .HasMaxLength(64)
            .IsRequired();

        // 售价必填，使用 decimal(18,2) 保存金额
        builder.Property(sku => sku.Price)
            .HasPrecision(18, 2)
            .IsRequired();

        // 库存必填，数据库默认值为 0
        builder.Property(sku => sku.Stock)
            .IsRequired()
            .HasDefaultValue(0);

        // 启用状态必填，数据库默认启用
        builder.Property(sku => sku.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        // 创建时间必填，保留微秒精度
        builder.Property(sku => sku.CreatedAtUtc)
            .HasColumnType("datetime(6)")
            .IsRequired();

        // 一个商品可以有多个 SKU，存在 SKU 时禁止直接删除商品
        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(sku => sku.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        // SKU 业务编码全站唯一
        builder.HasIndex(sku => sku.SkuCode)
            .IsUnique();

        // 创建唯一索引字段组合，同一商品不能重复创建相同规格组合
        builder.HasIndex(sku => new
        {
            sku.ProductId,
            sku.SpecificationKey
        })
            .IsUnique();

        // 创建查询索引字段组合，支持按商品和启用状态筛选并按编号排列
        builder.HasIndex(sku => new
        {
            sku.ProductId,
            sku.IsActive,
            sku.Id
        });
    }
}