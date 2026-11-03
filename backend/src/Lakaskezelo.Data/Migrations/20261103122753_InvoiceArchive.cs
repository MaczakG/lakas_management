using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lakaskezelo.Data.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceArchive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Invoices_PropertyId_PeriodYear_PeriodMonth",
                table: "Invoices");

            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAt",
                table: "Invoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ArchivedByUserId",
                table: "Invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_PropertyId_PeriodYear_PeriodMonth",
                table: "Invoices",
                columns: new[] { "PropertyId", "PeriodYear", "PeriodMonth" },
                unique: true,
                filter: "\"ArchivedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Invoices_PropertyId_PeriodYear_PeriodMonth",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "ArchivedByUserId",
                table: "Invoices");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_PropertyId_PeriodYear_PeriodMonth",
                table: "Invoices",
                columns: new[] { "PropertyId", "PeriodYear", "PeriodMonth" },
                unique: true);
        }
    }
}
