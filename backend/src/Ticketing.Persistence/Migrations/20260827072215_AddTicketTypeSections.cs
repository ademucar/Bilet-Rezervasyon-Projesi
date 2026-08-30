using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ticketing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketTypeSections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TicketTypeSections",
                columns: table => new
                {
                    TicketTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeatSectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketTypeSections", x => new { x.TicketTypeId, x.SeatSectionId });
                    table.ForeignKey(
                        name: "FK_TicketTypeSections_SeatSections_SeatSectionId",
                        column: x => x.SeatSectionId,
                        principalTable: "SeatSections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TicketTypeSections_TicketTypes_TicketTypeId",
                        column: x => x.TicketTypeId,
                        principalTable: "TicketTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ticket_type_sections_section",
                table: "TicketTypeSections",
                column: "SeatSectionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TicketTypeSections");
        }
    }
}
