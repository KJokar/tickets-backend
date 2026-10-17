using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Database.Migrations
{
    public partial class ExclusiveSupportChat : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Preserve existing messages rather than dropping and recreating their table.
            migrationBuilder.RenameTable(name: "TicketReplies", schema: "Ticketing", newName: "ChatMessages");
            migrationBuilder.RenameIndex(name: "IX_TicketReplies_AuthorUserId", schema: "Ticketing", table: "ChatMessages", newName: "IX_ChatMessages_AuthorUserId");
            migrationBuilder.RenameIndex(name: "IX_TicketReplies_TicketId_CreatedAt", schema: "Ticketing", table: "ChatMessages", newName: "IX_ChatMessages_TicketId_CreatedAt");
            migrationBuilder.Sql("""
                ALTER TABLE "Ticketing"."ChatMessages" RENAME CONSTRAINT "PK_TicketReplies" TO "PK_ChatMessages";
                ALTER TABLE "Ticketing"."ChatMessages" RENAME CONSTRAINT "FK_TicketReplies_Tickets_TicketId" TO "FK_ChatMessages_Tickets_TicketId";
                ALTER TABLE "Ticketing"."ChatMessages" RENAME CONSTRAINT "FK_TicketReplies_Users_AuthorUserId" TO "FK_ChatMessages_Users_AuthorUserId";
                """);
            migrationBuilder.AddColumn<Guid>(name: "ClientMessageId", schema: "Ticketing", table: "ChatMessages", type: "uuid", nullable: true);
            migrationBuilder.Sql("""UPDATE "Ticketing"."ChatMessages" SET "ClientMessageId" = gen_random_uuid();""");
            migrationBuilder.AlterColumn<Guid>(name: "ClientMessageId", schema: "Ticketing", table: "ChatMessages", type: "uuid", nullable: false,
                oldClrType: typeof(Guid), oldType: "uuid", oldNullable: true);
            migrationBuilder.CreateIndex(name: "IX_ChatMessages_TicketId_ClientMessageId", schema: "Ticketing", table: "ChatMessages",
                columns: new[] { "TicketId", "ClientMessageId" }, unique: true);
            migrationBuilder.DropIndex(name: "IX_Tickets_Department", schema: "Ticketing", table: "Tickets");
            migrationBuilder.DropColumn(name: "Department", schema: "Ticketing", table: "Tickets");
            migrationBuilder.AlterColumn<string>(name: "Priority", schema: "Ticketing", table: "Tickets", type: "character varying(20)", maxLength: 20,
                nullable: true, oldClrType: typeof(string), oldType: "character varying(20)", oldMaxLength: 20);
            migrationBuilder.AddColumn<DateTime>(name: "AcceptedAt", schema: "Ticketing", table: "Tickets", type: "timestamp with time zone", nullable: true);
            migrationBuilder.Sql("""
                UPDATE "Ticketing"."Tickets" SET "AcceptedAt" = COALESCE("UpdatedAt", "CreatedAt") WHERE "AssignedToId" IS NOT NULL;
                UPDATE "Ticketing"."Tickets" SET "Priority" = NULL WHERE "AssignedToId" IS NULL;
                UPDATE "Ticketing"."Tickets" SET "Status" = 'InProgress' WHERE "AssignedToId" IS NOT NULL AND "Status" = 'Open';
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_ChatMessages_TicketId_ClientMessageId", schema: "Ticketing", table: "ChatMessages");
            migrationBuilder.DropColumn(name: "ClientMessageId", schema: "Ticketing", table: "ChatMessages");
            migrationBuilder.DropColumn(name: "AcceptedAt", schema: "Ticketing", table: "Tickets");
            migrationBuilder.Sql("""UPDATE "Ticketing"."Tickets" SET "Priority" = 'Medium' WHERE "Priority" IS NULL;""");
            migrationBuilder.AlterColumn<string>(name: "Priority", schema: "Ticketing", table: "Tickets", type: "character varying(20)", maxLength: 20,
                nullable: false, oldClrType: typeof(string), oldType: "character varying(20)", oldMaxLength: 20, oldNullable: true);
            migrationBuilder.AddColumn<string>(name: "Department", schema: "Ticketing", table: "Tickets", type: "character varying(100)", maxLength: 100,
                nullable: false, defaultValue: "General Support");
            migrationBuilder.CreateIndex(name: "IX_Tickets_Department", schema: "Ticketing", table: "Tickets", column: "Department");
            migrationBuilder.Sql("""
                ALTER TABLE "Ticketing"."ChatMessages" RENAME CONSTRAINT "PK_ChatMessages" TO "PK_TicketReplies";
                ALTER TABLE "Ticketing"."ChatMessages" RENAME CONSTRAINT "FK_ChatMessages_Tickets_TicketId" TO "FK_TicketReplies_Tickets_TicketId";
                ALTER TABLE "Ticketing"."ChatMessages" RENAME CONSTRAINT "FK_ChatMessages_Users_AuthorUserId" TO "FK_TicketReplies_Users_AuthorUserId";
                """);
            migrationBuilder.RenameIndex(name: "IX_ChatMessages_AuthorUserId", schema: "Ticketing", table: "ChatMessages", newName: "IX_TicketReplies_AuthorUserId");
            migrationBuilder.RenameIndex(name: "IX_ChatMessages_TicketId_CreatedAt", schema: "Ticketing", table: "ChatMessages", newName: "IX_TicketReplies_TicketId_CreatedAt");
            migrationBuilder.RenameTable(name: "ChatMessages", schema: "Ticketing", newName: "TicketReplies");
        }
    }
}
