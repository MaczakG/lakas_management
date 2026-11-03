using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lakaskezelo.Data.Migrations
{
    /// <inheritdoc />
    public partial class InvoicePaid : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "InvoiceId",
                table: "TenantPayments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PaidAt",
                table: "Invoices",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantPayments_InvoiceId",
                table: "TenantPayments",
                column: "InvoiceId");

            migrationBuilder.AddForeignKey(
                name: "FK_TenantPayments_Invoices_InvoiceId",
                table: "TenantPayments",
                column: "InvoiceId",
                principalTable: "Invoices",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TenantPayments_Invoices_InvoiceId",
                table: "TenantPayments");

            migrationBuilder.DropIndex(
                name: "IX_TenantPayments_InvoiceId",
                table: "TenantPayments");

            migrationBuilder.DropColumn(
                name: "InvoiceId",
                table: "TenantPayments");

            migrationBuilder.DropColumn(
                name: "PaidAt",
                table: "Invoices");
        }
    }
}
