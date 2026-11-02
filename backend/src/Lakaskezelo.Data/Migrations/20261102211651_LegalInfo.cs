using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lakaskezelo.Data.Migrations
{
    /// <inheritdoc />
    public partial class LegalInfo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ImmediatePerformanceConsentAt",
                table: "SubscriptionPayments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OperatorAddress",
                table: "PlatformSettings",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OperatorEmail",
                table: "PlatformSettings",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OperatorName",
                table: "PlatformSettings",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OperatorPhone",
                table: "PlatformSettings",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OperatorRegistrationNumber",
                table: "PlatformSettings",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OperatorTaxNumber",
                table: "PlatformSettings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TermsVersion",
                table: "PlatformSettings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "2026-11-03");

            migrationBuilder.AddColumn<DateTime>(
                name: "TermsAcceptedAt",
                table: "Companies",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TermsVersion",
                table: "Companies",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Companies",
                keyColumn: "Id",
                keyValue: new Guid("d0000000-0000-4000-8000-000000000001"),
                columns: new[] { "TermsAcceptedAt", "TermsVersion" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "PlatformSettings",
                keyColumn: "Id",
                keyValue: new Guid("22222222-2222-4222-8222-222222222222"),
                columns: new[] { "OperatorAddress", "OperatorEmail", "OperatorName", "OperatorPhone", "OperatorRegistrationNumber", "OperatorTaxNumber", "TermsVersion" },
                values: new object[] { null, null, null, null, null, null, "2026-11-03" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImmediatePerformanceConsentAt",
                table: "SubscriptionPayments");

            migrationBuilder.DropColumn(
                name: "OperatorAddress",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "OperatorEmail",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "OperatorName",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "OperatorPhone",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "OperatorRegistrationNumber",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "OperatorTaxNumber",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "TermsVersion",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "TermsAcceptedAt",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "TermsVersion",
                table: "Companies");
        }
    }
}
