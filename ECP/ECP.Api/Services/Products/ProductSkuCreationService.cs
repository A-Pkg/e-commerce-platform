using ECP.Api.Constants.Stocks;
using ECP.Api.Data;
using ECP.Api.Dtos.Products;
using ECP.Api.Models.Products;
using ECP.Api.Models.Stocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MySql.Data.MySqlClient;

namespace ECP.Api.Services.Products;

/// <summary>
/// 创建商品 SKU，统一处理校验、规格关联和初始库存流水
/// </summary>
public class ProductSkuCreationService
{
    // decimal(18,2) 可以保存的最大金额
    private const decimal MaximumPrice = 9999999999999999.99m;

    // 仅在 SKU 编码本身冲突时，最多尝试生成三次
    private const int MaximumCodeAttempts = 3;

    // 数据库上下文
    private readonly AppDbContext _db;

    // 初始化数据库上下文
    public ProductSkuCreationService(AppDbContext db)
    {
        _db = db;
    }

    // 创建 SKU，操作用户编号由控制器从已验证身份中取得
    public async Task<ProductSkuCreationResult> CreateAsync(
        int productId,
        CreateProductSkuRequest request,
        int operatorUserId,
        CancellationToken cancellationToken)
    {
        // 服务参数本身不能为空
        ArgumentNullException.ThrowIfNull(request);

        // 检查商品编号
        if (productId <= 0)
        {
            return ProductSkuCreationResult.Fail(
                ProductSkuCreationFailure.InvalidInput,
                "商品编号必须大于 0");
        }

        // 检查操作用户编号
        if (operatorUserId <= 0)
        {
            return ProductSkuCreationResult.Fail(
                ProductSkuCreationFailure.OperatorNotFound,
                "当前登录身份无效，请重新登录");
        }

        // 保存价格快照，区分未填写和填写零元
        decimal? requestedPrice = request.Price;

        if (!requestedPrice.HasValue)
        {
            return ProductSkuCreationResult.Fail(
                ProductSkuCreationFailure.InvalidInput,
                "请输入 SKU 售价");
        }

        decimal price = requestedPrice.Value;

        // 金额必须符合现有数据库范围
        if (price < 0 || price > MaximumPrice)
        {
            return ProductSkuCreationResult.Fail(
                ProductSkuCreationFailure.InvalidInput,
                "售价超出允许范围");
        }

        // 拒绝需要数据库自动舍入的金额
        if (decimal.Round(price, 2) != price)
        {
            return ProductSkuCreationResult.Fail(
                ProductSkuCreationFailure.InvalidInput,
                "售价最多保留两位小数");
        }

        // 保存初始库存快照
        int initialStock = request.InitialStock;

        if (initialStock < 0)
        {
            return ProductSkuCreationResult.Fail(
                ProductSkuCreationFailure.InvalidInput,
                "初始库存不能小于 0");
        }

        // 允许空列表，但不允许列表本身为 null
        if (request.SpecificationValueIds is null)
        {
            return ProductSkuCreationResult.Fail(
                ProductSkuCreationFailure.InvalidInput,
                "规格值列表不能为 null");
        }

        // 创建编号快照，后续不再读取可变请求列表
        int[] valueIds = request.SpecificationValueIds.ToArray();

        if (valueIds.Any(valueId => valueId <= 0))
        {
            return ProductSkuCreationResult.Fail(
                ProductSkuCreationFailure.InvalidInput,
                "规格值编号必须大于 0");
        }

        // 同一个规格值不能重复提交
        if (valueIds.Distinct().Count() != valueIds.Length)
        {
            return ProductSkuCreationResult.Fail(
                ProductSkuCreationFailure.InvalidInput,
                "不能重复选择同一个规格值");
        }

        // 检查商品存在
        bool productExists = await _db.Products
            .AsNoTracking()
            .AnyAsync(
                product => product.Id == productId,
                cancellationToken);

        if (!productExists)
        {
            return ProductSkuCreationResult.Fail(
                ProductSkuCreationFailure.ProductNotFound,
                "该商品不存在");
        }

        // 检查操作用户存在，避免库存流水引用无效用户
        bool operatorExists = await _db.Users
            .AsNoTracking()
            .AnyAsync(
                user => user.Id == operatorUserId,
                cancellationToken);

        if (!operatorExists)
        {
            return ProductSkuCreationResult.Fail(
                ProductSkuCreationFailure.OperatorNotFound,
                "当前操作用户不存在，请重新登录");
        }

        // 创建已选规格值列表，空列表表示未选择任何规格
        List<SelectedSpecificationValue> selectedValues =
            new List<SelectedSpecificationValue>();

        if (valueIds.Length > 0)
        {
            // 查询编号对应的值，并通过所属规格限制到当前商品
            selectedValues = await (
                from specificationValue in
                    _db.ProductSpecificationValues.AsNoTracking()
                join specification in _db.ProductSpecifications.AsNoTracking()
                    on specificationValue.SpecificationId equals specification.Id
                where valueIds.Contains(specificationValue.Id)
                    && specification.ProductId == productId
                orderby specification.SortOrder, specification.Id
                // 创建用于校验、关联保存和展示的中间对象
                select new SelectedSpecificationValue
                {
                    SpecificationId = specification.Id,
                    SpecificationValueId = specificationValue.Id,
                    SpecificationName = specification.Name,
                    Value = specificationValue.Value
                })
                .ToListAsync(cancellationToken);

            // 数量不一致说明编号不存在，或所选值属于其他商品
            if (selectedValues.Count != valueIds.Length)
            {
                return ProductSkuCreationResult.Fail(
                    ProductSkuCreationFailure.InvalidInput,
                    "所选规格值不存在或不属于当前商品，请刷新后重新选择");
            }

            // 每种规格最多选择一个值，允许某些规格不选
            bool hasRepeatedSpecification = selectedValues
                .GroupBy(value => value.SpecificationId)
                .Any(group => group.Count() > 1);

            if (hasRepeatedSpecification)
            {
                return ProductSkuCreationResult.Fail(
                    ProductSkuCreationFailure.InvalidInput,
                    "同一种规格最多只能选择一个值");
            }
        }

        // 根据实际选择生成组合摘要，空列表也有固定摘要
        string specificationKey =
            ProductSkuIdentifierGenerator.CreateSpecificationKey(
                selectedValues.Select(value => (
                    SpecificationId: value.SpecificationId,
                    SpecificationValueId: value.SpecificationValueId)));

        // 包含停用 SKU，禁止同一商品重复创建相同组合
        bool combinationExists = await _db.ProductSkus
            .AsNoTracking()
            .AnyAsync(
                sku => sku.ProductId == productId
                    && sku.SpecificationKey == specificationKey,
                cancellationToken);

        if (combinationExists)
        {
            return DuplicateCombination(valueIds.Length == 0);
        }

        // 同一次创建中复用编码，仅编码本身冲突时重新生成
        string skuCode = ProductSkuIdentifierGenerator.CreateSkuCode();

        // SKU 和初始流水使用同一个创建时间
        DateTime createdAtUtc = DateTime.UtcNow;

        for (int attempt = 0; attempt < MaximumCodeAttempts; attempt++)
        {
            // 创建待保存的 SKU
            ProductSku newSku = new ProductSku
            {
                ProductId = productId,
                SkuCode = skuCode,
                SpecificationKey = specificationKey,
                Price = price,
                Stock = initialStock,
                IsActive = true,
                CreatedAtUtc = createdAtUtc
            };

            // 创建本次尝试的跟踪对象列表，失败后清理跟踪状态
            List<object> createdEntities = new List<object>();

            // 标记 SKU 主记录是否已经保存
            bool skuInserted = false;

            try
            {
                // 开启事务，任一步失败都不保留本次创建的数据
                await using IDbContextTransaction transaction =
                    await _db.Database.BeginTransactionAsync(cancellationToken);

                // 保存 SKU，取得数据库生成的编号
                _db.ProductSkus.Add(newSku);
                createdEntities.Add(newSku);
                await _db.SaveChangesAsync(cancellationToken);
                skuInserted = true;

                foreach (SelectedSpecificationValue value in selectedValues)
                {
                    // 创建 SKU 与已选规格值的关联
                    ProductSkuSpecificationValue relation =
                        new ProductSkuSpecificationValue
                        {
                            SkuId = newSku.Id,
                            SpecificationId = value.SpecificationId,
                            SpecificationValueId = value.SpecificationValueId
                        };

                    _db.ProductSkuSpecificationValues.Add(relation);
                    createdEntities.Add(relation);
                }

                // 零库存没有数量变化，不创建零数量流水
                if (initialStock > 0)
                {
                    // 创建初始入库流水，与 SKU 创建放在同一个事务
                    StockMovement movement = new StockMovement
                    {
                        MovementNo = FormattableString.Invariant(
                            $"INIT:{newSku.Id}"),
                        SkuId = newSku.Id,
                        QuantityChange = initialStock,
                        BalanceAfter = initialStock,
                        Reason = StockMovementReasons.InitialStock,
                        OrderId = null,
                        OperatorUserId = operatorUserId,
                        CreatedAtUtc = createdAtUtc
                    };

                    _db.StockMovements.Add(movement);
                    createdEntities.Add(movement);
                }

                // 保存规格关联和必要的初始库存流水
                await _db.SaveChangesAsync(cancellationToken);

                // 全部保存成功后提交事务
                await transaction.CommitAsync(cancellationToken);

                // 创建响应对象，未选择的规格不参与展示
                ProductSkuResponse response = new ProductSkuResponse
                {
                    Id = newSku.Id,
                    ProductId = newSku.ProductId,
                    SkuCode = newSku.SkuCode,
                    SpecificationText = string.Join(
                        " / ",
                        selectedValues.Select(
                            value => $"{value.SpecificationName}：{value.Value}")),
                    Price = newSku.Price,
                    Stock = newSku.Stock,
                    IsActive = newSku.IsActive
                };

                return ProductSkuCreationResult.Success(response);
            }
            catch (DbUpdateException exception) when (
                !skuInserted
                && exception.InnerException is MySqlException mysqlException
                && mysqlException.Number == 1062)
            {
                // 事务释放后已经回滚，移除本次尝试的跟踪对象
                DetachCreatedEntities(createdEntities);

                // 并发创建相同组合时，返回明确的重复提示
                bool duplicateCombination = await _db.ProductSkus
                    .AsNoTracking()
                    .AnyAsync(
                        sku => sku.ProductId == productId
                            && sku.SpecificationKey == specificationKey,
                        cancellationToken);

                if (duplicateCombination)
                {
                    return DuplicateCombination(valueIds.Length == 0);
                }

                // 只有确认业务编码冲突，才重新生成编码
                bool codeExists = await _db.ProductSkus
                    .AsNoTracking()
                    .AnyAsync(
                        sku => sku.SkuCode == skuCode,
                        cancellationToken);

                if (!codeExists)
                {
                    throw;
                }

                skuCode = ProductSkuIdentifierGenerator.CreateSkuCode();
            }
            catch (DbUpdateException exception) when (
                exception.InnerException is MySqlException mysqlException
                && mysqlException.Number == 1452)
            {
                // 商品、规格值或操作用户可能在检查后被删除
                DetachCreatedEntities(createdEntities);

                return ProductSkuCreationResult.Fail(
                    ProductSkuCreationFailure.RelatedDataChanged,
                    "关联数据已发生变化，请刷新后重新创建 SKU");
            }
            catch
            {
                // 其他异常继续向上交给请求异常处理，同时清理跟踪状态
                DetachCreatedEntities(createdEntities);
                throw;
            }
        }

        return ProductSkuCreationResult.Fail(
            ProductSkuCreationFailure.SkuCodeConflict,
            "SKU 编码生成失败，请稍后重试");
    }

    // 返回重复规格组合的业务结果
    private static ProductSkuCreationResult DuplicateCombination(
        bool emptyCombination)
    {
        return ProductSkuCreationResult.Fail(
            ProductSkuCreationFailure.DuplicateCombination,
            emptyCombination
                ? "该商品已存在未选择规格的 SKU"
                : "该商品已存在相同规格组合的 SKU");
    }

    // 清理本次创建尝试产生的跟踪对象，供失败或编码重试使用
    private void DetachCreatedEntities(List<object> createdEntities)
    {
        foreach (object entity in createdEntities)
        {
            _db.Entry(entity).State = EntityState.Detached;
        }
    }

    /// <summary>
    /// 已验证的规格值及其展示信息
    /// </summary>
    private sealed class SelectedSpecificationValue
    {
        // 所属规格编号
        public int SpecificationId { get; set; }

        // 规格值编号
        public int SpecificationValueId { get; set; }

        // 规格名称
        public string SpecificationName { get; set; } = string.Empty;

        // 规格值内容
        public string Value { get; set; } = string.Empty;
    }
}