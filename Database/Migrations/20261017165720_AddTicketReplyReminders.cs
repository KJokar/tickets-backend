using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Database.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketReplyReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TicketReplyReminders",
                schema: "Ticketing",
                columns: table => new
                {
                    TicketId = table.Column<int>(type: "integer", nullable: false),
                    AgentId = table.Column<int>(type: "integer", nullable: false),
                    LastReplyAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextReminderAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NotificationId = table.Column<Guid>(type: "uuid", nullable: true),
                    RaisedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketReplyReminders", x => x.TicketId);
                    table.ForeignKey(
                        name: "FK_TicketReplyReminders_Tickets_TicketId",
                        column: x => x.TicketId,
                        principalSchema: "Ticketing",
                        principalTable: "Tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TicketReplyReminders_Users_AgentId",
                        column: x => x.AgentId,
                        principalSchema: "Identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TicketReplyReminders_AgentId",
                schema: "Ticketing",
                table: "TicketReplyReminders",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_TicketReplyReminders_NextReminderAt",
                schema: "Ticketing",
                table: "TicketReplyReminders",
                column: "NextReminderAt",
                filter: "\"NextReminderAt\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TicketReplyReminders",
                schema: "Ticketing");
        }
    }
}
