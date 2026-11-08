using ECP.Api.Models.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECP.Api.Data.Configurations.Products;

/// <summary>
/// 商品规格值的数据库映射配置
/// </summary>
public class ProductSpecificationValueConfiguration
    : IEntityTypeConfiguration<ProductSpecificationValue>
{
    // 配置规格值表的字段、规格关联、备用键和索引
    public void Configure(EntityTypeBuilder<ProductSpecificationValue> builder)
    {
        // 数据库表名
        builder.ToTable("product_specification_values");

        // 规格值编号作为主键
        builder.HasKey(specificationValue => specificationValue.Id);

        // 规格值编号由数据库自动生成
        builder.Property(specificationValue => specificationValue.Id)
            .ValueGeneratedOnAdd();

        // 每个规格值必须有所属规格
        builder.Property(specificationValue => specificationValue.SpecificationId)
            .IsRequired();

        // 规格值内容必填，最多 100 个字符
        builder.Property(specificationValue => specificationValue.Value)
            .HasMaxLength(100)
            .IsRequired();

        // 排序值必填，数据库默认值为 0
        builder.Property(specificationValue => specificationValue.SortOrder)
            .IsRequired()
            .HasDefaultValue(0);

        // 创建时间必填，保留微秒精度
        builder.Property(specificationValue => specificationValue.CreatedAtUtc)
            .HasColumnType("datetime(6)")
            .IsRequired();

        // 一个规格可以有多个值，存在规格值时禁止直接删除所属规格
        builder.HasOne<ProductSpecification>()
            .WithMany()
            .HasForeignKey(specificationValue => specificationValue.SpecificationId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_spec_values_specifications");

        // 创建组合备用键，供后续 SKU 关联同时引用规格值编号和所属规格编号
        builder.HasAlternateKey(specificationValue => new
        {
            specificationValue.Id,
            specificationValue.SpecificationId
        })
            .HasName("AK_spec_values_id_spec");

        // 创建唯一索引字段组合，同一种规格下不允许重复定义相同值
        builder.HasIndex(specificationValue => new
        {
            specificationValue.SpecificationId,
            specificationValue.Value
        })
            .IsUnique();

        // 创建排序索引字段组合，支持按规格查询并按排序值、编号排列
        builder.HasIndex(specificationValue => new
        {
            specificationValue.SpecificationId,
            specificationValue.SortOrder,
            specificationValue.Id
        });
    }
}