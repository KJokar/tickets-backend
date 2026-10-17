using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Database.Migrations
{
    /// <inheritdoc />
    public partial class CompleteAuthenticationAndTicketAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tickets_Users_OwnerUserId",
                schema: "Ticketing",
                table: "Tickets");

            migrationBuilder.DropIndex(
                name: "IX_Users_Email",
                schema: "Identity",
                table: "Users");

            migrationBuilder.RenameColumn(
                name: "DisplayName",
                schema: "Identity",
                table: "Users",
                newName: "FullName");

            migrationBuilder.RenameColumn(
                name: "OwnerUserId",
                schema: "Ticketing",
                table: "Tickets",
                newName: "CreatedById");

            migrationBuilder.RenameIndex(
                name: "IX_Tickets_OwnerUserId",
                schema: "Ticketing",
                table: "Tickets",
                newName: "IX_Tickets_CreatedById");

            migrationBuilder.AddColumn<string>(
                name: "NormalizedEmail",
                schema: "Identity",
                table: "Users",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                schema: "Identity",
                table: "Users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Active");

            // Preserve existing email identities and suspended accounts before
            // enforcing the new unique index and removing the legacy flag.
            migrationBuilder.Sql("""
                UPDATE "Identity"."Users"
                SET "NormalizedEmail" = upper(btrim("Email")),
                    "Status" = CASE WHEN "IsActive" THEN 'Active' ELSE 'Suspended' END;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "NormalizedEmail",
                schema: "Identity",
                table: "Users",
                type: "character varying(254)",
                maxLength: 254,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(254)",
                oldMaxLength: 254,
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "IsActive",
                schema: "Identity",
                table: "Users");

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                schema: "Identity",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AssignedToId",
                schema: "Ticketing",
                table: "Tickets",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AuthSessions",
                schema: "Identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuthSessions_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "Identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_NormalizedEmail",
                schema: "Identity",
                table: "Users",
                column: "NormalizedEmail",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_AssignedToId_Status",
                schema: "Ticketing",
                table: "Tickets",
                columns: new[] { "AssignedToId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AuthSessions_ExpiresAt",
                schema: "Identity",
                table: "AuthSessions",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_AuthSessions_UserId_ExpiresAt",
                schema: "Identity",
                table: "AuthSessions",
                columns: new[] { "UserId", "ExpiresAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_Tickets_Users_AssignedToId",
                schema: "Ticketing",
                table: "Tickets",
                column: "AssignedToId",
                principalSchema: "Identity",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Tickets_Users_CreatedById",
                schema: "Ticketing",
                table: "Tickets",
                column: "CreatedById",
                principalSchema: "Identity",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tickets_Users_AssignedToId",
                schema: "Ticketing",
                table: "Tickets");

            migrationBuilder.DropForeignKey(
                name: "FK_Tickets_Users_CreatedById",
                schema: "Ticketing",
                table: "Tickets");

            migrationBuilder.DropTable(
                name: "AuthSessions",
                schema: "Identity");

            migrationBuilder.DropIndex(
                name: "IX_Users_NormalizedEmail",
                schema: "Identity",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Tickets_AssignedToId_Status",
                schema: "Ticketing",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "NormalizedEmail",
                schema: "Identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                schema: "Identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "AssignedToId",
                schema: "Ticketing",
                table: "Tickets");

            migrationBuilder.RenameColumn(
                name: "FullName",
                schema: "Identity",
                table: "Users",
                newName: "DisplayName");

            migrationBuilder.RenameColumn(
                name: "CreatedById",
                schema: "Ticketing",
                table: "Tickets",
                newName: "OwnerUserId");

            migrationBuilder.RenameIndex(
                name: "IX_Tickets_CreatedById",
                schema: "Ticketing",
                table: "Tickets",
                newName: "IX_Tickets_OwnerUserId");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                schema: "Identity",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.Sql("""
                UPDATE "Identity"."Users"
                SET "IsActive" = ("Status" = 'Active');
                """);

            migrationBuilder.DropColumn(
                name: "Status",
                schema: "Identity",
                table: "Users");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                schema: "Identity",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Tickets_Users_OwnerUserId",
                schema: "Ticketing",
                table: "Tickets",
                column: "OwnerUserId",
                principalSchema: "Identity",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
