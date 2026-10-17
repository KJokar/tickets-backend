using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Database.Migrations
{
    /// <inheritdoc />
    public partial class InitialRequestAsChatMessage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ReadAt",
                schema: "Ticketing",
                table: "ChatMessages",
                type: "timestamp with time zone",
                nullable: true);


            // Preserve existing read state and move every original request body into the conversation.
            // Existing message IDs and receipt cursors are intentionally preserved.
            migrationBuilder.Sql("""
                UPDATE "Ticketing"."ChatMessages" AS m
                SET "ReadAt" = (SELECT MIN(r."ReadAt") FROM "Ticketing"."ChatReadReceipts" AS r
                    WHERE r."TicketId" = m."TicketId" AND r."UserId" <> m."AuthorUserId"
                      AND r."LastReadMessageId" >= m."Id");
                INSERT INTO "Ticketing"."ChatMessages"
                    ("TicketId", "AuthorUserId", "ClientMessageId", "Body", "CreatedAt", "ReadAt")
                SELECT t."Id", t."CreatedById", md5('ticket-first-message:' || t."Id"::text)::uuid,
                    t."Description", t."CreatedAt",
                    (SELECT MIN(r."ReadAt") FROM "Ticketing"."ChatReadReceipts" AS r
                     WHERE r."TicketId" = t."Id" AND r."UserId" <> t."CreatedById")
                FROM "Ticketing"."Tickets" AS t;
                """);

            migrationBuilder.DropColumn(
                name: "Description",
                schema: "Ticketing",
                table: "Tickets");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReadAt",
                schema: "Ticketing",
                table: "ChatMessages");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                schema: "Ticketing",
                table: "Tickets",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: "");
            migrationBuilder.Sql("""
                UPDATE "Ticketing"."Tickets" AS t SET "Description" = COALESCE(
                    (SELECT m."Body" FROM "Ticketing"."ChatMessages" AS m WHERE m."TicketId" = t."Id"
                     ORDER BY m."CreatedAt", m."Id" LIMIT 1), '');
                """);
        }
    }
}
