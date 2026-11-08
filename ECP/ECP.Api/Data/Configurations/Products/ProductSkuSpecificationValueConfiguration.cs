using ECP.Api.Models.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECP.Api.Data.Configurations.Products;

/// <summary>
/// SKU 与规格值关联表的数据库映射配置
/// </summary>
public class ProductSkuSpecificationValueConfiguration
    : IEntityTypeConfiguration<ProductSkuSpecificationValue>
{
    // 配置关联表的联合主键、外键和索引
    public void Configure(EntityTypeBuilder<ProductSkuSpecificationValue> builder)
    {
        // 数据库表名
        builder.ToTable("product_sku_specification_values");

        // 创建联合主键字段组合，同一 SKU 对同一种规格只能选择一个值
        builder.HasKey(link => new
        {
            link.SkuId,
            link.SpecificationId
        });

        // SKU 编号必填
        builder.Property(link => link.SkuId)
            .IsRequired();

        // 规格编号必填
        builder.Property(link => link.SpecificationId)
            .IsRequired();

        // 规格值编号必填
        builder.Property(link => link.SpecificationValueId)
            .IsRequired();

        // 关联所属 SKU，存在规格选择记录时禁止直接删除 SKU
        builder.HasOne<ProductSku>()
            .WithMany()
            .HasForeignKey(link => link.SkuId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_sku_spec_values_skus");

        // 创建组合外键和目标键字段组合，同时验证规格值编号与所属规格编号
        builder.HasOne<ProductSpecificationValue>()
            .WithMany()
            .HasForeignKey(link => new
            {
                link.SpecificationValueId,
                link.SpecificationId
            })
            .HasPrincipalKey(specificationValue => new
            {
                specificationValue.Id,
                specificationValue.SpecificationId
            })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_sku_spec_values_values");

        // 创建索引字段组合，支持组合外键查询和规格值引用检查
        builder.HasIndex(link => new
        {
            link.SpecificationValueId,
            link.SpecificationId
        })
            .HasDatabaseName("IX_sku_spec_values_value_spec");
    }
}