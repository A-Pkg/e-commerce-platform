using ECP.Api.Constants.Users;

namespace ECP.Api.Models.Users;

public class AppUser
{
    // 用户编号
    public int Id { get; set; }

    // 登录账号
    public string Username { get; set; } = string.Empty;

    // 用户角色，新创建的用户默认是普通用户
    public string Role { get; set; } = UserRoles.User;

    // 账号是否启用，新用户默认启用；停用与软删除分别记录
    public bool IsActive { get; set; } = true;

    // 密码哈希，不能存放明文密码
    public string PasswordHash { get; set; } = string.Empty;

    // 认证版本；修改密码后递增，使该账号之前签发的令牌失效
    public int TokenVersion { get; set; }

    // 是否已伪删除，新用户默认未删除
    public bool IsDeleted { get; set; }

    // 账号被标记删除的 UTC 时间，未删除时为空
    public DateTime? DeletedAtUtc { get; set; }

    // 注册时间，统一使用 UTC 时间
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}