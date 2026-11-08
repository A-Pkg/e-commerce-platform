using ECP.Api.Models.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECP.Api.Data.Configurations.Products;

/// <summary>
/// 商品规格的数据库映射配置
/// </summary>
public class ProductSpecificationConfiguration
    : IEntityTypeConfiguration<ProductSpecification>
{
    // 配置规格表的字段、商品关联、唯一约束和排序索引
    public void Configure(EntityTypeBuilder<ProductSpecification> builder)
    {
        // 数据库表名
        builder.ToTable("product_specifications");

        // 规格编号作为主键
        builder.HasKey(specification => specification.Id);

        // 规格编号由数据库自动生成
        builder.Property(specification => specification.Id)
            .ValueGeneratedOnAdd();

        // 每个规格必须有所属商品
        builder.Property(specification => specification.ProductId)
            .IsRequired();

        // 规格名称必填，最多 50 个字符
        builder.Property(specification => specification.Name)
            .HasMaxLength(50)
            .IsRequired();

        // 排序值必填，数据库默认值为 0
        builder.Property(specification => specification.SortOrder)
            .IsRequired()
            .HasDefaultValue(0);

        // 创建时间必填，保留微秒精度
        builder.Property(specification => specification.CreatedAtUtc)
            .HasColumnType("datetime(6)")
            .IsRequired();

        // 一个商品可以有多个规格，存在规格关联时禁止直接删除商品
        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(specification => specification.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        // 创建唯一索引字段组合，同一个商品不能重复定义同名规格
        builder.HasIndex(specification => new
        {
            specification.ProductId,
            specification.Name
        })
            .IsUnique();

        // 创建排序索引字段组合，支持按商品查询并按排序值、编号排列
        builder.HasIndex(specification => new
        {
            specification.ProductId,
            specification.SortOrder,
            specification.Id
        });
    }
}