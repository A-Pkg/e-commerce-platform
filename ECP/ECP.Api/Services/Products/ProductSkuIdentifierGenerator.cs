using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ECP.Api.Services.Products;

/// <summary>
/// 生成 SKU 业务编码和规格组合摘要
/// </summary>
public static class ProductSkuIdentifierGenerator
{
    // 生成 SKU 业务编码：SKU + 14 位 UTC 时间 + 32 位 GUID
    public static string CreateSkuCode()
    {
        // 按固定文化格式生成 UTC 时间
        string utcTime = DateTime.UtcNow.ToString(
            "yyyyMMddHHmmss",
            CultureInfo.InvariantCulture);

        // 生成不带横线的 GUID
        string randomPart = Guid.NewGuid().ToString("N");

        // 完整编码共 49 个字符
        return "SKU" + utcTime + randomPart;
    }

    // 按已选规格和值的编号生成稳定的组合摘要
    public static string CreateSpecificationKey(
        IEnumerable<(int SpecificationId, int SpecificationValueId)> specificationValues)
    {
        // 参数不能为 null，允许传入空集合
        ArgumentNullException.ThrowIfNull(specificationValues);

        // 创建排序后的列表，使相同选择不受输入顺序影响
        List<(int SpecificationId, int SpecificationValueId)> orderedValues =
            specificationValues
                .OrderBy(value => value.SpecificationId)
                .ThenBy(value => value.SpecificationValueId)
                .ToList();

        // 拼接规范化内容，例如“1:11|2:21”
        string source = string.Join(
            "|",
            orderedValues.Select(value =>
                FormattableString.Invariant(
                    $"{value.SpecificationId}:{value.SpecificationValueId}")));

        // 将规范化内容转换为 UTF-8 字节
        byte[] sourceBytes = Encoding.UTF8.GetBytes(source);

        // 计算 SHA-256，空集合则计算空字符串的摘要
        byte[] hashBytes = SHA256.HashData(sourceBytes);

        // 返回 64 位小写十六进制字符串
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}