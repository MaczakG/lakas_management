using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lakaskezelo.Data.Migrations
{
    /// <inheritdoc />
    public partial class DocumentVersionsAndTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DocumentContents_Documents_DocumentId",
                table: "DocumentContents");

            migrationBuilder.RenameColumn(
                name: "DocumentId",
                table: "DocumentContents",
                newName: "VersionId");

            migrationBuilder.AddColumn<int>(
                name: "CurrentVersion",
                table: "Documents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "DocumentTypeId",
                table: "Documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DocumentTypesInitialized",
                table: "Companies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "DocumentEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentEvents_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentEvents_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DocumentEvents_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "DocumentTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentTypes_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DocumentVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    UploadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentVersions_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentVersions_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DocumentVersions_Users_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.UpdateData(
                table: "Companies",
                keyColumn: "Id",
                keyValue: new Guid("d0000000-0000-4000-8000-000000000001"),
                column: "DocumentTypesInitialized",
                value: false);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_DocumentTypeId",
                table: "Documents",
                column: "DocumentTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentEvents_CompanyId",
                table: "DocumentEvents",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentEvents_DocumentId_At",
                table: "DocumentEvents",
                columns: new[] { "DocumentId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentEvents_UserId",
                table: "DocumentEvents",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTypes_CompanyId",
                table: "DocumentTypes",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentVersions_CompanyId",
                table: "DocumentVersions",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentVersions_DocumentId_Number",
                table: "DocumentVersions",
                columns: new[] { "DocumentId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentVersions_UploadedByUserId",
                table: "DocumentVersions",
                column: "UploadedByUserId");

            // Adatmentés: a meglévő dokumentumok 1. verziója (Id = a dokumentum Id-je, így a DocumentContents sorai
            // — amelyek eddig a dokumentum Id-jével voltak kulcsolva — érvényesek maradnak), feltöltési napló-bejegyzés,
            // a dokumentummal már rendelkező cégeknek az alapértelmezett típusok, és a régi kategória átírása típusra.
            migrationBuilder.Sql("""
                UPDATE "Documents" SET "CurrentVersion" = 1;

                INSERT INTO "DocumentVersions" ("Id", "CompanyId", "DocumentId", "Number", "FileName", "ContentType", "SizeBytes", "StorageKey", "UploadedAt", "UploadedByUserId")
                SELECT "Id", "CompanyId", "Id", 1, "FileName", "ContentType", "SizeBytes", "StorageKey", "UploadedAt", "UploadedByUserId" FROM "Documents";

                INSERT INTO "DocumentEvents" ("Id", "CompanyId", "DocumentId", "Type", "VersionNumber", "UserId", "At")
                SELECT gen_random_uuid(), "CompanyId", "Id", 'Uploaded', 1, "UploadedByUserId", "UploadedAt" FROM "Documents";

                INSERT INTO "DocumentTypes" ("Id", "CompanyId", "Name", "SortOrder", "CreatedAt")
                SELECT gen_random_uuid(), c."Id", t.name, t.ord, now()
                FROM "Companies" c
                CROSS JOIN (VALUES ('Szerződés', 0), ('Rezsi számla', 1), ('Jegyzőkönyv', 2), ('Fotó', 3), ('Egyéb', 4)) AS t(name, ord)
                WHERE EXISTS (SELECT 1 FROM "Documents" d WHERE d."CompanyId" = c."Id");

                UPDATE "Companies" c SET "DocumentTypesInitialized" = true
                WHERE EXISTS (SELECT 1 FROM "DocumentTypes" t WHERE t."CompanyId" = c."Id");

                UPDATE "Documents" d SET "DocumentTypeId" = t."Id"
                FROM "DocumentTypes" t
                WHERE t."CompanyId" = d."CompanyId" AND t."Name" = CASE d."Category"
                    WHEN 'Contract' THEN 'Szerződés' WHEN 'UtilityBill' THEN 'Rezsi számla' WHEN 'Handover' THEN 'Jegyzőkönyv'
                    WHEN 'Photo' THEN 'Fotó' ELSE 'Egyéb' END;
                """);
            migrationBuilder.DropColumn(
                name: "Category",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "StorageKey",
                table: "Documents");

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentContents_DocumentVersions_VersionId",
                table: "DocumentContents",
                column: "VersionId",
                principalTable: "DocumentVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Documents_DocumentTypes_DocumentTypeId",
                table: "Documents",
                column: "DocumentTypeId",
                principalTable: "DocumentTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DocumentContents_DocumentVersions_VersionId",
                table: "DocumentContents");

            migrationBuilder.DropForeignKey(
                name: "FK_Documents_DocumentTypes_DocumentTypeId",
                table: "Documents");

            migrationBuilder.DropTable(
                name: "DocumentEvents");

            migrationBuilder.DropTable(
                name: "DocumentTypes");

            migrationBuilder.DropTable(
                name: "DocumentVersions");

            migrationBuilder.DropIndex(
                name: "IX_Documents_DocumentTypeId",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "CurrentVersion",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "DocumentTypeId",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "DocumentTypesInitialized",
                table: "Companies");

            migrationBuilder.RenameColumn(
                name: "VersionId",
                table: "DocumentContents",
                newName: "DocumentId");

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "Documents",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "StorageKey",
                table: "Documents",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentContents_Documents_DocumentId",
                table: "DocumentContents",
                column: "DocumentId",
                principalTable: "Documents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
