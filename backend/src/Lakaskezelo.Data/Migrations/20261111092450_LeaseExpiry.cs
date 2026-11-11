using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lakaskezelo.Data.Migrations
{
    /// <inheritdoc />
    public partial class LeaseExpiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "LeaseEndDate",
                table: "Tenants",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "LeaseExpiryNoticeFor",
                table: "Tenants",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LeaseExpiryEmails",
                table: "AppSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LeaseExpiryNoticeDays",
                table: "AppSettings",
                type: "integer",
                nullable: false,
                defaultValue: 30);   // a meglévő cégek is 30 nappal indulnak

            migrationBuilder.UpdateData(
                table: "AppSettings",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111111"),
                columns: new[] { "LeaseExpiryEmails", "LeaseExpiryNoticeDays" },
                values: new object[] { null, 30 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LeaseEndDate",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "LeaseExpiryNoticeFor",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "LeaseExpiryEmails",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "LeaseExpiryNoticeDays",
                table: "AppSettings");
        }
    }
}
