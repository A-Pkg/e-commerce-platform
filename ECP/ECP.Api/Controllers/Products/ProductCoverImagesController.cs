using ECP.Api.Constants.Users;
using ECP.Api.Data;
using ECP.Api.Dtos.Products;
using ECP.Api.Models.Products;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Text.RegularExpressions;

namespace ECP.Api.Controllers.Products;

/// <summary>
/// 商品封面上传、更换与图片读取接口
/// </summary>
[ApiController]
[Route("api/products/{productId:int}/cover-image")]
public class ProductCoverImagesController : ControllerBase
{
    // 单张图片最大允许 2MB
    private const long MaximumImageBytes = 2 * 1024 * 1024;

    // 数据库上下文
    private readonly AppDbContext _db;

    // 后端保存封面文件的目录
    private readonly string _imageDirectory;

    // 记录服务端异常，不将内部错误直接返回前端
    private readonly ILogger<ProductCoverImagesController> _logger;

    // 初始化数据库、图片目录和日志服务
    public ProductCoverImagesController(
        AppDbContext db,
        IWebHostEnvironment environment,
        ILogger<ProductCoverImagesController> logger)
    {
        _db = db;
        _logger = logger;
        _imageDirectory = Path.Combine(
            environment.ContentRootPath,
            "Uploads",
            "ProductCovers");
    }

    // 管理员上传或更换封面，绝对路由覆盖控制器的公开读取路由
    [Authorize(Roles = UserRoles.Admin)]
    [HttpPut("/api/admin/products/{productId:int}/cover-image")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(3 * 1024 * 1024)]
    public async Task<IActionResult> Upload(
        [FromRoute] int productId,
        [FromForm] UploadProductCoverRequest request,
        CancellationToken cancellationToken)
    {
        // 必须为已经保存的有效商品
        if (productId <= 0)
        {
            return BadRequest(new
            {
                message = "商品编号必须大于 0"
            });
        }

        IFormFile? file = request.File;

        if (file is null || file.Length == 0)
        {
            return BadRequest(new
            {
                message = "请选择非空图片"
            });
        }

        if (file.Length > MaximumImageBytes)
        {
            return BadRequest(new
            {
                message = "图片不能超过 2MB"
            });
        }

        // 将大小受限的文件读入内存，供签名检查和保存使用
        using MemoryStream buffer = new MemoryStream();

        await file.CopyToAsync(buffer, cancellationToken);

        byte[] imageBytes = buffer.ToArray();

        // 再次按实际读取大小检查
        if (imageBytes.Length == 0
            || imageBytes.Length > MaximumImageBytes)
        {
            return BadRequest(new
            {
                message = "图片大小必须在 1 字节到 2MB 之间"
            });
        }

        // PNG 的固定文件头，不依赖客户端提供的后缀和 Content-Type
        byte[] pngSignature = new byte[]
        {
            137, 80, 78, 71, 13, 10, 26, 10
        };

        string extension;

        if (imageBytes.Take(8).SequenceEqual(pngSignature))
        {
            extension = ".png";
        }
        else if (imageBytes.Length >= 3
            && imageBytes[0] == 255
            && imageBytes[1] == 216
            && imageBytes[2] == 255)
        {
            extension = ".jpg";
        }
        else
        {
            return BadRequest(new
            {
                message = "只支持 JPG/JPEG 或 PNG 图片"
            });
        }

        // 使用后端生成的文件名，不使用管理员电脑上的原文件名
        string fileName = Guid.NewGuid().ToString("N") + extension;
        string filePath = Path.Combine(_imageDirectory, fileName);

        // 数据库存访问地址，不存磁盘绝对路径
        string imageUrl = FormattableString.Invariant(
            $"/api/products/{productId}/cover-image/{fileName}");

        try
        {
            // 同一商品的封面修改串行执行，避免同时首次上传产生重复封面
            await using IDbContextTransaction transaction =
                await _db.Database.BeginTransactionAsync(cancellationToken);

            // 锁住所属商品；直接执行 SQL，不在 FOR UPDATE 外再组合查询
            List<Product> lockedProducts = await _db.Products
                .FromSqlInterpolated(
                    $"SELECT * FROM `products` WHERE `Id` = {productId} FOR UPDATE")
                .ToListAsync(cancellationToken);

            if (lockedProducts.Count == 0)
            {
                return NotFound(new
                {
                    message = "该商品不存在"
                });
            }

            // 将排序最前的一条图片记录作为封面
            ProductImage? cover = await _db.ProductImages
                .Where(image => image.ProductId == productId)
                .OrderBy(image => image.SortOrder)
                .ThenBy(image => image.Id)
                .FirstOrDefaultAsync(cancellationToken);

            // 首次上传时自动创建图片目录
            Directory.CreateDirectory(_imageDirectory);

            // 先完整保存新文件，再修改数据库引用
            await using (FileStream imageStream = new FileStream(
                filePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                useAsync: true))
            {
                await imageStream.WriteAsync(
                    imageBytes,
                    cancellationToken);
            }

            if (cover is null)
            {
                // 创建第一条封面记录
                cover = new ProductImage
                {
                    ProductId = productId,
                    ImageUrl = imageUrl,
                    SortOrder = 0,
                    CreatedAtUtc = DateTime.UtcNow
                };

                _db.ProductImages.Add(cover);
            }
            else
            {
                // 更换封面只更新引用，不重复增加图片记录
                cover.ImageUrl = imageUrl;
            }

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            // 返回前端可以直接使用的图片访问地址
            return Ok(new
            {
                productId,
                imageUrl
            });
        }
        catch (IOException exception)
        {
            _logger.LogError(
                exception,
                "保存商品 {ProductId} 的封面文件失败",
                productId);

            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new
                {
                    message = "图片保存失败，请稍后重试"
                });
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.LogError(
                exception,
                "商品封面目录无法写入");

            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new
                {
                    message = "图片保存失败，请联系管理员"
                });
        }
        catch (DbUpdateException exception)
        {
            _logger.LogError(
                exception,
                "保存商品 {ProductId} 的封面记录失败",
                productId);

            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new
                {
                    message = "保存商品封面失败，请稍后重试"
                });
        }
    }

    // 读取商品当前封面，管理员预览和商城展示共用
    [AllowAnonymous]
    [HttpGet("{fileName?}")]
    public async Task<IActionResult> Get(
        [FromRoute] int productId,
        [FromRoute] string? fileName,
        CancellationToken cancellationToken)
    {
        if (productId <= 0)
        {
            return BadRequest(new
            {
                message = "商品编号必须大于 0"
            });
        }

        // 下架商品也允许读取封面，供后台编辑时预览
        string? imageUrl = await _db.ProductImages
            .AsNoTracking()
            .Where(image => image.ProductId == productId)
            .OrderBy(image => image.SortOrder)
            .ThenBy(image => image.Id)
            .Select(image => image.ImageUrl)
            .FirstOrDefaultAsync(cancellationToken);

        if (imageUrl is null)
        {
            return NotFound();
        }

        // 只读取本接口生成的本地封面路径
        string prefix = FormattableString.Invariant(
            $"/api/products/{productId}/cover-image/");

        if (!imageUrl.StartsWith(prefix, StringComparison.Ordinal))
        {
            return NotFound();
        }

        string storedFileName = imageUrl[prefix.Length..];

        // 严格限定文件名，禁止将请求参数作为任意磁盘路径
        if (!Regex.IsMatch(
                storedFileName,
                @"\A[a-f0-9]{32}\.(jpg|png)\z"))
        {
            return NotFound();
        }

        // 带文件名访问时，必须与当前封面一致
        if (fileName is not null
            && !string.Equals(
                fileName,
                storedFileName,
                StringComparison.Ordinal))
        {
            return NotFound();
        }

        string filePath = Path.Combine(
            _imageDirectory,
            storedFileName);

        if (!System.IO.File.Exists(filePath))
        {
            return NotFound();
        }

        string contentType = storedFileName.EndsWith(
            ".png",
            StringComparison.Ordinal)
            ? "image/png"
            : "image/jpeg";

        // 更换封面后重新读取，避免继续显示旧图
        Response.Headers["Cache-Control"] = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";

        return PhysicalFile(filePath, contentType);
    }
}