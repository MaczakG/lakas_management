using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lakaskezelo.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPropertyInvoicePrefixAndInvoiceConversionNote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NextInvoiceNumber",
                table: "AppSettings");

            migrationBuilder.AddColumn<string>(
                name: "InvoicePrefix",
                table: "Properties",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RentConversionNote",
                table: "Invoices",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InvoicePrefix",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "RentConversionNote",
                table: "Invoices");

            migrationBuilder.AddColumn<int>(
                name: "NextInvoiceNumber",
                table: "AppSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.UpdateData(
                table: "AppSettings",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111111"),
                column: "NextInvoiceNumber",
                value: 1);
        }
    }
}
