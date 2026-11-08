using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace ECP.Api.Extensions;

/// <summary>
/// OpenAPI 文档注册扩展，为 Swagger 提供 JWT 授权入口
/// </summary>
public static class OpenApiServiceExtensions
{
    // 文档中的 Bearer 认证方案名称
    private const string BearerSchemeName = "Bearer";

    // 注册 OpenAPI 文档及 Bearer 授权说明
    public static IServiceCollection AddApiOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi(options =>
        {
            // 在文档中声明 Bearer 认证方案
            options.AddDocumentTransformer(ConfigureBearerDocumentAsync);

            // 为需要授权的接口添加 Bearer 认证要求
            options.AddOperationTransformer(ConfigureBearerOperationAsync);

            // 为需要防伪请求头的接口添加输入框
            options.AddOperationTransformer(ConfigureCsrfOperationAsync);
        });

        return services;
    }

    // 配置文档中的 Bearer 认证方案，供 Swagger 显示授权入口
    private static Task ConfigureBearerDocumentAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        // 文档尚无组件集合时，创建组件集合
        document.Components ??= new OpenApiComponents();

        // 尚无认证方案集合时，创建认证方案集合
        document.Components.SecuritySchemes ??=
            new Dictionary<string, IOpenApiSecurityScheme>();

        // 创建 HTTP Bearer 认证方案，Swagger 会自动添加 Bearer 前缀
        document.Components.SecuritySchemes[BearerSchemeName] =
            new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "仅填写登录返回的 accessToken，无需添加 Bearer 前缀。"
            };

        return Task.CompletedTask;
    }

    // 根据接口的授权标记，配置 Swagger 请求时是否携带令牌
    private static Task ConfigureBearerOperationAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        // 检查接口是否明确允许匿名访问
        bool allowsAnonymous = context.Description.ActionDescriptor
            .EndpointMetadata
            .OfType<IAllowAnonymous>()
            .Any();

        // 检查控制器或接口方法是否声明了授权要求
        bool requiresAuthorization = context.Description.ActionDescriptor
            .EndpointMetadata
            .OfType<IAuthorizeData>()
            .Any();

        // 匿名接口以及未声明授权要求的接口，保持公开访问说明
        if (allowsAnonymous || !requiresAuthorization)
        {
            return Task.CompletedTask;
        }

        // 获取当前接口所属的文档，用于引用 Bearer 认证方案
        OpenApiDocument document = context.Document
            ?? throw new InvalidOperationException(
                "生成接口授权说明时未获取到 OpenAPI 文档。");

        // 创建当前接口的 Bearer 认证要求
        OpenApiSecurityRequirement requirement =
            new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(
                    BearerSchemeName, document)] = new List<string>()
            };

        // 尚无接口认证要求集合时，创建集合
        operation.Security ??= new List<OpenApiSecurityRequirement>();

        // 将 Bearer 认证要求加入当前接口
        operation.Security.Add(requirement);

        return Task.CompletedTask;
    }

    // 让登录、刷新和退出接口显示防伪请求头输入框
    private static Task ConfigureCsrfOperationAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        bool requiresCsrf = context.Description.ActionDescriptor
            .EndpointMetadata
            .OfType<ValidateAntiForgeryTokenAttribute>()
            .Any();

        if (requiresCsrf)
        {
            operation.Parameters ??= new List<IOpenApiParameter>();

            operation.Parameters.Add(new OpenApiParameter
            {
                Name = "X-CSRF-TOKEN",
                In = ParameterLocation.Header,
                Required = true,
                Description =
                    "填写 GET /api/auth/csrf 返回的 requestToken。",

                Schema = new OpenApiSchema
                {
                    Type = JsonSchemaType.String
                }
            });
        }

        return Task.CompletedTask;
    }
}