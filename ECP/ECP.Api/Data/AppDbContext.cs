using ECP.Api.Models.Auth;
using ECP.Api.Models.Carts;
using ECP.Api.Models.Orders;
using ECP.Api.Models.Payments;
using ECP.Api.Models.Products;
using ECP.Api.Models.Stocks;
using ECP.Api.Models.Users;
using Microsoft.EntityFrameworkCore;

namespace ECP.Api.Data
{
    public class AppDbContext:DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext>options):base(options)
        {
        }

        #region 用户
        // 用户数据的查询和操作入口
        public DbSet<AppUser> Users => Set<AppUser>();

        // 用户收货地址数据的查询和操作入口
        public DbSet<UserAddress> UserAddresses => Set<UserAddress>();

        // 登录会话的查询与操作入口
        public DbSet<AuthSession> AuthSessions => Set<AuthSession>();

        // 刷新令牌查询入口
        public DbSet<AuthRefreshToken> AuthRefreshTokens => Set<AuthRefreshToken>();
        #endregion

        #region 商品
        // 商品分类数据的查询和操作入口
        public DbSet<Category> Categories => Set<Category>();

        // 商品数据的查询和操作入口
        public DbSet<Product> Products => Set<Product>();

        // 商品图片数据的查询和操作入口
        public DbSet<ProductImage> ProductImages => Set<ProductImage>();

        // 商品规格数据的查询和操作入口
        public DbSet<ProductSpecification> ProductSpecifications => Set<ProductSpecification>();

        // 商品规格值数据的查询和操作入口
        public DbSet<ProductSpecificationValue> ProductSpecificationValues => Set<ProductSpecificationValue>();

        // 商品 SKU 数据的查询和操作入口
        public DbSet<ProductSku> ProductSkus => Set<ProductSku>();

        // SKU 与规格值关联数据的查询和操作入口
        public DbSet<ProductSkuSpecificationValue> ProductSkuSpecificationValues => Set<ProductSkuSpecificationValue>();
        #endregion

        #region 购物车
        // 购物车条目数据的查询和操作入口
        public DbSet<CartItem> CartItems => Set<CartItem>();
        #endregion

        #region 订单
        // 订单数据的查询和操作入口
        public DbSet<Order> Orders => Set<Order>();

        // 订单明细数据的查询和操作入口
        public DbSet<OrderItem> OrderItems => Set<OrderItem>();
        #endregion

        #region 支付
        // 支付记录数据的查询和操作入口
        public DbSet<Payment> Payments => Set<Payment>();
        #endregion

        #region 库存
        // 库存流水数据的查询和操作入口
        public DbSet<StockMovement> StockMovements => Set<StockMovement>();
        #endregion

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // 自动加载当前程序集中的所有实体配置类
            // 包括 Configurations 下各个子文件夹里的配置类
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        }
    }
}
