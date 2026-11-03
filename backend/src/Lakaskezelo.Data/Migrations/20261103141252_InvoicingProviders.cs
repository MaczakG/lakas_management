using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lakaskezelo.Data.Migrations
{
    /// <inheritdoc />
    public partial class InvoicingProviders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BillingoPartnerId",
                table: "Tenants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RentVat",
                table: "Properties",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UtilityVat",
                table: "Properties",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "Invoices",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderDocumentId",
                table: "Invoices",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsBusiness",
                table: "Companies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "BillingoApiKey",
                table: "AppSettings",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BillingoBlockId",
                table: "AppSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultRentVat",
                table: "AppSettings",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "TAM");

            migrationBuilder.AddColumn<string>(
                name: "DefaultUtilityVat",
                table: "AppSettings",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "TAM");

            migrationBuilder.AddColumn<string>(
                name: "InvoiceProvider",
                table: "AppSettings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SzamlazzAgentKey",
                table: "AppSettings",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SzamlazzInvoicePrefix",
                table: "AppSettings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "AppSettings",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111111"),
                columns: new[] { "BillingoApiKey", "BillingoBlockId", "DefaultRentVat", "DefaultUtilityVat", "InvoiceProvider", "SzamlazzAgentKey", "SzamlazzInvoicePrefix" },
                values: new object[] { null, null, "TAM", "TAM", null, null, null });

            migrationBuilder.UpdateData(
                table: "Companies",
                keyColumn: "Id",
                keyValue: new Guid("d0000000-0000-4000-8000-000000000001"),
                column: "IsBusiness",
                value: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BillingoPartnerId",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "RentVat",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "UtilityVat",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "ProviderDocumentId",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "IsBusiness",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "BillingoApiKey",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "BillingoBlockId",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "DefaultRentVat",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "DefaultUtilityVat",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "InvoiceProvider",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "SzamlazzAgentKey",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "SzamlazzInvoicePrefix",
                table: "AppSettings");
        }
    }
}
