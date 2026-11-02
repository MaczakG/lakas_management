using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lakaskezelo.Data.Migrations
{
    /// <inheritdoc />
    public partial class RolesAndVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EmailVerifiedAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Role",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Purpose",
                table: "PasswordResetTokens",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Kézzel: a meglévő felhasználók (Role=0) adminisztrátorok, és az e-mail címük igazoltnak számít
            // (eddig is ezzel léptek be, és ide kapták a belépési kódot).
            migrationBuilder.Sql("""UPDATE "Users" SET "EmailVerifiedAt" = "CreatedAt";""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmailVerifiedAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Role",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Purpose",
                table: "PasswordResetTokens");
        }
    }
}
