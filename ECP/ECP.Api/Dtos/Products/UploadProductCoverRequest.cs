using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace ECP.Api.Dtos.Products;

/// <summary>
/// 管理员上传商品封面的请求数据
/// </summary>
public class UploadProductCoverRequest
{
    // 本次选择的图片文件
    [Required(ErrorMessage = "请选择商品封面")]
    public IFormFile? File { get; set; }
}