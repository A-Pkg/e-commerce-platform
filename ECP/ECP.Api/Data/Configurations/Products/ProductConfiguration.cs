using ECP.Api.Models.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECP.Api.Data.Configurations.Products;

/// <summary>
/// 商品主表的数据库映射配置
/// </summary>
public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    // 配置商品表的字段、索引和分类关联
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        // 数据库表名
        builder.ToTable("products");

        // 商品编号作为主键
        builder.HasKey(product => product.Id);

        // 商品编号由数据库自动生成
        builder.Property(product => product.Id)
            .ValueGeneratedOnAdd();

        // 每个商品必须有所属分类
        builder.Property(product => product.CategoryId)
            .IsRequired();

        // 商品名称不能为 NULL，最多 200 个字符
        builder.Property(product => product.Name)
            .HasMaxLength(200)
            .IsRequired();

        // 商品描述不能为 NULL，最多 2000 个字符，数据库默认空字符串
        builder.Property(product => product.Description)
            .HasMaxLength(2000)
            .IsRequired()
            .HasDefaultValue(string.Empty);

        // 上架状态必填，数据库默认不上架
        builder.Property(product => product.IsOnSale)
            .IsRequired()
            .HasDefaultValue(false);

        // 创建时间必填，保留微秒精度
        builder.Property(product => product.CreatedAtUtc)
            .HasColumnType("datetime(6)")
            .IsRequired();

        // 一个分类可以有多个商品，有商品关联时禁止直接删除该分类
        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(product => product.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        // 为按分类、上架状态筛选并按编号排序建立复合索引
        builder.HasIndex(product => new
        {
            product.CategoryId,
            product.IsOnSale,
            product.Id
        });
    }
}