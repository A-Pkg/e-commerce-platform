using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ECP.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserSoftDeleteFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                table: "users",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "users",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "users");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "users");
        }
    }
}
