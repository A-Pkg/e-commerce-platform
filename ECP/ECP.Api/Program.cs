using ECP.Api.Data;
using ECP.Api.Extensions;
using ECP.Api.Models.Users;
using ECP.Api.Options.Auth;
using ECP.Api.Options.Orders;
using ECP.Api.Services.Auth;
using ECP.Api.Services.Orders;
using ECP.Api.Services.Products;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();

// 注册 OpenAPI 文档及 Swagger 的 Bearer 授权入口
builder.Services.AddApiOpenApi();

// 读取数据库连接字符串
string connectionString = builder.Configuration.GetConnectionString("EcpDb")
    ??throw new InvalidOperationException("未配置EcpDb数据库连接字符串");

// 注册数据库上下文
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseMySQL(connectionString);
});

// 注册密码哈希服务
builder.Services.AddScoped<IPasswordHasher<AppUser>,PasswordHasher<AppUser>>();

// 注册 JWT 配置校验器
builder.Services.AddSingleton<IValidateOptions<JwtOptions>,JwtOptionsValidator>();

// 绑定 JWT 配置，并在启动时执行校验
builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection("Jwt"))
    .ValidateOnStart();

// 绑定订单配置，并在启动时检查付款期限
builder.Services.AddOptions<OrderOptions>()
    .Bind(builder.Configuration.GetSection("Orders"))
    .Validate(
        options => options.PaymentTimeoutMinutes > 0,
        "Orders:PaymentTimeoutMinutes 必须大于零")
    .ValidateOnStart();

// 注册支付宝配置、配置校验器和 SDK 客户端
builder.Services.AddAlipayPayments(builder.Configuration);

// 注册 JWT 令牌生成服务
builder.Services.AddScoped<JwtTokenService>();

// 注册当前登录用户会话服务
builder.Services.AddScoped<AuthSessionService>();

// 为使用刷新 Cookie 的登录、刷新和退出接口注册防伪校验
// 身份认证仍使用 JWT Bearer
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";

    options.Cookie.Name = "ecp.csrf";
    options.Cookie.Path = "/api/auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
});

// 注册 SKU 创建服务，与数据库上下文使用相同的请求作用域
builder.Services.AddScoped<ProductSkuCreationService>();

// 注册公共订单关闭服务，与数据库上下文使用相同作用域
builder.Services.AddScoped<OrderClosingService>();

// 注册订单超时扫描任务，随后端启动和停止
builder.Services.AddHostedService<OrderExpirationBackgroundService>();

// 注册 JWT 认证和授权服务
builder.Services.AddJwtAuthentication();

// 注册账号接口的请求频率限制
builder.Services.AddAuthRateLimiting();

WebApplication app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    app.UseSwaggerUI(option =>
    {
        option.SwaggerEndpoint("/openapi/v1.json", "ECP.Api v1");
    });
}

app.UseHttpsRedirection();

// 匹配目标接口，使后续中间件能够读取接口上的限流策略
app.UseRouting();

// 启用认证中间件，验证请求中携带的 JWT，并建立当前用户身份
app.UseAuthentication();

// 执行目标接口对应的请求频率限制
app.UseRateLimiter();

// 启用认证和授权中间件，用于检查当前用户是否有权访问目标接口
app.UseAuthorization();

app.MapControllers();

app.Run();
