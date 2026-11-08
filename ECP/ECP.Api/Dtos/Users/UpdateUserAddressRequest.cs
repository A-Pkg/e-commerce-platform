using System.ComponentModel.DataAnnotations;

namespace ECP.Api.Dtos.Users;

/// <summary>
/// 修改收货地址的请求数据
/// </summary>
public class UpdateUserAddressRequest
{
    // 收货人姓名，必填且最多 50 个字符
    [Required(ErrorMessage = "请输入收货人姓名")]
    [StringLength(50, ErrorMessage = "收货人姓名最多 50 个字符")]
    public string RecipientName { get; set; } = string.Empty;

    // 联系电话使用字符串保存，仅允许数字，最多 32 位
    [Required(ErrorMessage = "请输入联系电话")]
    [StringLength(32, ErrorMessage = "联系电话最多 32 位")]
    [RegularExpression(@"^[0-9]+$", ErrorMessage = "联系电话只能包含数字")]
    public string Phone { get; set; } = string.Empty;

    // 完整收货地址，必填且最多 500 个字符
    [Required(ErrorMessage = "请输入完整收货地址")]
    [StringLength(500, ErrorMessage = "完整收货地址最多 500 个字符")]
    public string Address { get; set; } = string.Empty;
}