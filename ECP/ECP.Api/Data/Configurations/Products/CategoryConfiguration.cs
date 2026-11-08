using ECP.Api.Models.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECP.Api.Data.Configurations.Products;

/// <summary>
/// 商品分类的数据库映射配置
/// </summary>
public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    // 配置分类表的字段、主键和索引
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        // 数据库表名
        builder.ToTable("categories");

        // 分类编号作为主键
        builder.HasKey(category => category.Id);

        // 分类编号由数据库自动生成
        builder.Property(category => category.Id)
            .ValueGeneratedOnAdd();

        // 父分类编号允许为 NULL，表示当前分类没有父分类
        builder.Property(category => category.ParentId)
            .IsRequired(false);

        // 分类关联同一张表中的父分类，存在子分类时禁止直接删除父分类
        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(category => category.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        // 分类名称不能为 NULL，最多 100 个字符
        builder.Property(category => category.Name)
            .HasMaxLength(100)
            .IsRequired();

        // 分类名称不允许重复，由数据库保证唯一性
        builder.HasIndex(category => category.Name)
            .IsUnique();

        // 排序值必填，数据库默认值为 0
        builder.Property(category => category.SortOrder)
            .IsRequired()
            .HasDefaultValue(0);

        // 启用状态必填，数据库默认启用
        builder.Property(category => category.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        // 创建时间必填，保留微秒精度
        builder.Property(category => category.CreatedAtUtc)
            .HasColumnType("datetime(6)")
            .IsRequired();

        // 为筛选启用分类、按排序值和编号排序建立复合索引
        builder.HasIndex(category => new
        {
            category.IsActive,
            category.SortOrder,
            category.Id
        });
    }
}