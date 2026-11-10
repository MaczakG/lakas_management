using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lakaskezelo.Data.Migrations
{
    /// <inheritdoc />
    public partial class UtilityAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "InvoiceAttachments",
                table: "Tenants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AttachmentsAvailable",
                table: "Invoices",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AttachmentsSent",
                table: "Invoices",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "UtilityCostEntryId",
                table: "Documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_UtilityCostEntryId",
                table: "Documents",
                column: "UtilityCostEntryId");

            migrationBuilder.AddForeignKey(
                name: "FK_Documents_UtilityCostEntries_UtilityCostEntryId",
                table: "Documents",
                column: "UtilityCostEntryId",
                principalTable: "UtilityCostEntries",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Documents_UtilityCostEntries_UtilityCostEntryId",
                table: "Documents");

            migrationBuilder.DropIndex(
                name: "IX_Documents_UtilityCostEntryId",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "InvoiceAttachments",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "AttachmentsAvailable",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "AttachmentsSent",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "UtilityCostEntryId",
                table: "Documents");
        }
    }
}
