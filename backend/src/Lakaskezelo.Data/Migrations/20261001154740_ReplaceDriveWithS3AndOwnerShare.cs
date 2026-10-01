using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lakaskezelo.Data.Migrations
{
    // Kézzel javítva: az EF a Drive-oszlopokat átnevezésnek vette (pl. GoogleOAuthRefreshToken →
    // S3Region), ami a régi tokent/titkot új beállításként hagyta volna meg — törlés + új oszlop kell.
    /// <inheritdoc />
    public partial class ReplaceDriveWithS3AndOwnerShare : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DriveFolderId",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "PdfDriveFileId",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "GoogleConnectedEmail",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "GoogleOAuthClientId",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "PdfDriveLink",
                table: "Invoices");

            migrationBuilder.AddColumn<string>(
                name: "PdfStorageKey",
                table: "Invoices",
                type: "text",
                nullable: true);

            migrationBuilder.DropColumn(
                name: "GoogleOAuthRefreshToken",
                table: "AppSettings");

            migrationBuilder.AddColumn<string>(
                name: "S3Region",
                table: "AppSettings",
                type: "text",
                nullable: true);

            migrationBuilder.DropColumn(
                name: "GoogleOAuthClientSecret",
                table: "AppSettings");

            migrationBuilder.AddColumn<string>(
                name: "S3BucketName",
                table: "AppSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Share",
                table: "PropertyOwners",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Share",
                table: "PropertyOwners");

            migrationBuilder.DropColumn(
                name: "PdfStorageKey",
                table: "Invoices");

            migrationBuilder.AddColumn<string>(
                name: "PdfDriveLink",
                table: "Invoices",
                type: "text",
                nullable: true);

            migrationBuilder.DropColumn(
                name: "S3Region",
                table: "AppSettings");

            migrationBuilder.AddColumn<string>(
                name: "GoogleOAuthRefreshToken",
                table: "AppSettings",
                type: "text",
                nullable: true);

            migrationBuilder.DropColumn(
                name: "S3BucketName",
                table: "AppSettings");

            migrationBuilder.AddColumn<string>(
                name: "GoogleOAuthClientSecret",
                table: "AppSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DriveFolderId",
                table: "Properties",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PdfDriveFileId",
                table: "Invoices",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GoogleConnectedEmail",
                table: "AppSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GoogleOAuthClientId",
                table: "AppSettings",
                type: "text",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "AppSettings",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111111"),
                columns: new[] { "GoogleConnectedEmail", "GoogleOAuthClientId" },
                values: new object[] { null, null });
        }
    }
}
