using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace ECP.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOrdersPaymentsAndStockMovements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "orders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    OrderNo = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    RequestId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, defaultValue: "PendingPayment"),
                    ItemsAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ShippingFee = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    PayableAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    RecipientName = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    RecipientPhone = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    ShippingAddress = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false),
                    Remark = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    ShippingCompany = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    TrackingNumber = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    PaidAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ShippedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_orders", x => x.Id);
                    table.CheckConstraint("CK_orders_amounts_nonnegative", "`ItemsAmount` >= 0 AND `ShippingFee` >= 0 AND `PayableAmount` >= 0");
                    table.CheckConstraint("CK_orders_payable_amount", "`PayableAmount` = `ItemsAmount` + `ShippingFee`");
                    table.CheckConstraint("CK_orders_status", "`Status` IN ('PendingPayment', 'Paid', 'Shipped', 'Completed', 'Cancelled')");
                    table.ForeignKey(
                        name: "FK_orders_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "order_items",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    SkuId = table.Column<int>(type: "int", nullable: false),
                    ProductName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    SkuCode = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    SpecificationText = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false),
                    ProductImageUrl = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: true),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    Subtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_items", x => x.Id);
                    table.CheckConstraint("CK_order_items_quantity_positive", "`Quantity` > 0");
                    table.CheckConstraint("CK_order_items_subtotal", "`Subtotal` = `UnitPrice` * `Quantity`");
                    table.CheckConstraint("CK_order_items_unit_price_nonnegative", "`UnitPrice` >= 0");
                    table.ForeignKey(
                        name: "FK_order_items_orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_order_items_product_skus_SkuId",
                        column: x => x.SkuId,
                        principalTable: "product_skus",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_order_items_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    PaymentNo = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Provider = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, defaultValue: "Mock"),
                    ProviderTransactionId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, defaultValue: "Pending"),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    PaidAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payments", x => x.Id);
                    table.CheckConstraint("CK_payments_amount_nonnegative", "`Amount` >= 0");
                    table.CheckConstraint("CK_payments_status", "`Status` IN ('Pending', 'Succeeded', 'Failed', 'Closed')");
                    table.ForeignKey(
                        name: "FK_payments_orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "stock_movements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    MovementNo = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    SkuId = table.Column<int>(type: "int", nullable: false),
                    QuantityChange = table.Column<int>(type: "int", nullable: false),
                    BalanceAfter = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    OrderId = table.Column<int>(type: "int", nullable: true),
                    OperatorUserId = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_movements", x => x.Id);
                    table.CheckConstraint("CK_stock_movements_balance_nonnegative", "`BalanceAfter` >= 0");
                    table.CheckConstraint("CK_stock_movements_quantity_change", "`QuantityChange` <> 0");
                    table.CheckConstraint("CK_stock_movements_reason", "`Reason` IN ('InitialStock', 'OrderDeduction', 'OrderCancellation', 'AdminAdjustment')");
                    table.ForeignKey(
                        name: "FK_stock_movements_orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_movements_product_skus_SkuId",
                        column: x => x.SkuId,
                        principalTable: "product_skus",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_movements_users_OperatorUserId",
                        column: x => x.OperatorUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_order_items_OrderId_SkuId",
                table: "order_items",
                columns: new[] { "OrderId", "SkuId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_items_ProductId",
                table: "order_items",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_order_items_SkuId",
                table: "order_items",
                column: "SkuId");

            migrationBuilder.CreateIndex(
                name: "IX_orders_OrderNo",
                table: "orders",
                column: "OrderNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_orders_Status_ExpiresAtUtc_Id",
                table: "orders",
                columns: new[] { "Status", "ExpiresAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_orders_UserId_CreatedAtUtc_Id",
                table: "orders",
                columns: new[] { "UserId", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_orders_UserId_RequestId",
                table: "orders",
                columns: new[] { "UserId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payments_OrderId_CreatedAtUtc_Id",
                table: "payments",
                columns: new[] { "OrderId", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_payments_PaymentNo",
                table: "payments",
                column: "PaymentNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payments_Provider_ProviderTransactionId",
                table: "payments",
                columns: new[] { "Provider", "ProviderTransactionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_MovementNo",
                table: "stock_movements",
                column: "MovementNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_OperatorUserId",
                table: "stock_movements",
                column: "OperatorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_OrderId_Id",
                table: "stock_movements",
                columns: new[] { "OrderId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_SkuId_CreatedAtUtc_Id",
                table: "stock_movements",
                columns: new[] { "SkuId", "CreatedAtUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_items");

            migrationBuilder.DropTable(
                name: "payments");

            migrationBuilder.DropTable(
                name: "stock_movements");

            migrationBuilder.DropTable(
                name: "orders");
        }
    }
}
