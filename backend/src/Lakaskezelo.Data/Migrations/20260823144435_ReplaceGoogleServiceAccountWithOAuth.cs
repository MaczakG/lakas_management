using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lakaskezelo.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceGoogleServiceAccountWithOAuth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "GoogleServiceAccountJson",
                table: "AppSettings",
                newName: "GoogleOAuthRefreshToken");

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

            migrationBuilder.AddColumn<string>(
                name: "GoogleOAuthClientSecret",
                table: "AppSettings",
                type: "text",
                nullable: true);

            // A RenameColumn a régi service account JSON-t egyszerűen átnevezte
            // GoogleOAuthRefreshToken-re — az ott maradt (érvénytelen) adatot itt nullázzuk ki,
            // különben a rendszer tévesen "csatlakoztatottnak" látná a Google Drive-ot.
            migrationBuilder.UpdateData(
                table: "AppSettings",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111111"),
                columns: new[] { "GoogleConnectedEmail", "GoogleOAuthClientId", "GoogleOAuthClientSecret", "GoogleOAuthRefreshToken" },
                values: new object[] { null, null, null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GoogleConnectedEmail",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "GoogleOAuthClientId",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "GoogleOAuthClientSecret",
                table: "AppSettings");

            migrationBuilder.RenameColumn(
                name: "GoogleOAuthRefreshToken",
                table: "AppSettings",
                newName: "GoogleServiceAccountJson");
        }
    }
}
