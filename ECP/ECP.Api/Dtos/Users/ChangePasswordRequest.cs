using System.ComponentModel.DataAnnotations;

namespace ECP.Api.Dtos.Users;

/// <summary>
/// 当前登录用户提交的修改密码信息
/// </summary>
public class ChangePasswordRequest
{
    // 原密码保持原样，不去除首尾空格
    [Required(ErrorMessage = "请输入原密码")]
    [StringLength(128, ErrorMessage = "原密码不能超过 128 个字符")]
    public string CurrentPassword { get; set; } = string.Empty;

    // 新密码沿用注册时的长度规则
    [Required(ErrorMessage = "请输入新密码")]
    [StringLength(
        128,
        MinimumLength = 8,
        ErrorMessage = "新密码长度必须为 8～128 个字符")]
    public string NewPassword { get; set; } = string.Empty;

    // 两次新密码必须完全一致
    [Required(ErrorMessage = "请再次输入新密码")]
    [Compare(
        nameof(NewPassword),
        ErrorMessage = "两次输入的新密码不一致")]
    public string ConfirmNewPassword { get; set; } = string.Empty;
}