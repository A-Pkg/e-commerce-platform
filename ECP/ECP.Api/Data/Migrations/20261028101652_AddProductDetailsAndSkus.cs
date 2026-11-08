using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace ECP.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProductDetailsAndSkus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "product_images",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    ImageUrl = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_images", x => x.Id);
                    table.ForeignKey(
                        name: "FK_product_images_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "product_skus",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    SkuCode = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    SpecificationKey = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Stock = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_skus", x => x.Id);
                    table.CheckConstraint("CK_product_skus_price_nonnegative", "`Price` >= 0");
                    table.CheckConstraint("CK_product_skus_stock_nonnegative", "`Stock` >= 0");
                    table.ForeignKey(
                        name: "FK_product_skus_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "product_specifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_specifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_product_specifications_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "product_specification_values",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    SpecificationId = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_specification_values", x => x.Id);
                    table.UniqueConstraint("AK_spec_values_id_spec", x => new { x.Id, x.SpecificationId });
                    table.ForeignKey(
                        name: "FK_spec_values_specifications",
                        column: x => x.SpecificationId,
                        principalTable: "product_specifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "product_sku_specification_values",
                columns: table => new
                {
                    SkuId = table.Column<int>(type: "int", nullable: false),
                    SpecificationId = table.Column<int>(type: "int", nullable: false),
                    SpecificationValueId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_sku_specification_values", x => new { x.SkuId, x.SpecificationId });
                    table.ForeignKey(
                        name: "FK_sku_spec_values_skus",
                        column: x => x.SkuId,
                        principalTable: "product_skus",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sku_spec_values_values",
                        columns: x => new { x.SpecificationValueId, x.SpecificationId },
                        principalTable: "product_specification_values",
                        principalColumns: new[] { "Id", "SpecificationId" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_product_images_ProductId_SortOrder_Id",
                table: "product_images",
                columns: new[] { "ProductId", "SortOrder", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_sku_spec_values_value_spec",
                table: "product_sku_specification_values",
                columns: new[] { "SpecificationValueId", "SpecificationId" });

            migrationBuilder.CreateIndex(
                name: "IX_product_skus_ProductId_IsActive_Id",
                table: "product_skus",
                columns: new[] { "ProductId", "IsActive", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_product_skus_ProductId_SpecificationKey",
                table: "product_skus",
                columns: new[] { "ProductId", "SpecificationKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_product_skus_SkuCode",
                table: "product_skus",
                column: "SkuCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_product_specification_values_SpecificationId_SortOrder_Id",
                table: "product_specification_values",
                columns: new[] { "SpecificationId", "SortOrder", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_product_specification_values_SpecificationId_Value",
                table: "product_specification_values",
                columns: new[] { "SpecificationId", "Value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_product_specifications_ProductId_Name",
                table: "product_specifications",
                columns: new[] { "ProductId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_product_specifications_ProductId_SortOrder_Id",
                table: "product_specifications",
                columns: new[] { "ProductId", "SortOrder", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product_images");

            migrationBuilder.DropTable(
                name: "product_sku_specification_values");

            migrationBuilder.DropTable(
                name: "product_skus");

            migrationBuilder.DropTable(
                name: "product_specification_values");

            migrationBuilder.DropTable(
                name: "product_specifications");
        }
    }
}
