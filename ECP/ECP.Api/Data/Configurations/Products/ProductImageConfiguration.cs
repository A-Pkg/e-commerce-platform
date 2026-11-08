using ECP.Api.Models.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECP.Api.Data.Configurations.Products;

/// <summary>
/// 商品图片的数据库映射配置
/// </summary>
public class ProductImageConfiguration : IEntityTypeConfiguration<ProductImage>
{
    // 配置图片表的字段、商品关联和排序索引
    public void Configure(EntityTypeBuilder<ProductImage> builder)
    {
        // 数据库表名
        builder.ToTable("product_images");

        // 图片记录编号作为主键
        builder.HasKey(image => image.Id);

        // 图片记录编号由数据库自动生成
        builder.Property(image => image.Id)
            .ValueGeneratedOnAdd();

        // 每张图片必须有所属商品
        builder.Property(image => image.ProductId)
            .IsRequired();

        // 图片地址必填，最多 1024 个字符
        builder.Property(image => image.ImageUrl)
            .HasMaxLength(1024)
            .IsRequired();

        // 排序值必填，数据库默认值为 0
        builder.Property(image => image.SortOrder)
            .IsRequired()
            .HasDefaultValue(0);

        // 创建时间必填，保留微秒精度
        builder.Property(image => image.CreatedAtUtc)
            .HasColumnType("datetime(6)")
            .IsRequired();

        // 一个商品可以有多张图片，存在图片关联时禁止直接删除商品
        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(image => image.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        // 创建索引字段组合，支持按商品查询并按排序值、编号排列
        builder.HasIndex(image => new
        {
            image.ProductId,
            image.SortOrder,
            image.Id
        });
    }
}