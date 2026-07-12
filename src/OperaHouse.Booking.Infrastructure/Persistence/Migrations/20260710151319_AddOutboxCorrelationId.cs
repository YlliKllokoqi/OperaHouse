using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OperaHouse.Booking.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxCorrelationId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CorrelationId",
                table: "OutboxMessages",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CorrelationId",
                table: "OutboxMessages");
        }
    }
}
