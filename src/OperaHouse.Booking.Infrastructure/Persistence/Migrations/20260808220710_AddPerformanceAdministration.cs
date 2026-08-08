using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OperaHouse.Booking.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddPerformanceAdministration : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "CancellationReason",
            table: "Performances",
            type: "character varying(1000)",
            maxLength: 1000,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "CancelledAt",
            table: "Performances",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "Capacity",
            table: "Performances",
            type: "integer",
            nullable: false,
            defaultValue: 1);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "CreatedAt",
            table: "Performances",
            type: "timestamp with time zone",
            nullable: false,
            defaultValueSql: "CURRENT_TIMESTAMP");

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "PublishedAt",
            table: "Performances",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Status",
            table: "Performances",
            type: "character varying(50)",
            maxLength: 50,
            nullable: false,
            defaultValue: "Draft");

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "UpdatedAt",
            table: "Performances",
            type: "timestamp with time zone",
            nullable: false,
            defaultValueSql: "CURRENT_TIMESTAMP");

        migrationBuilder.Sql(
            """
            UPDATE "Performances"
            SET "Capacity" = GREATEST("AvailableSeats", 1),
                "Status" = 'Published',
                "PublishedAt" = "CreatedAt";
            """);

        migrationBuilder.CreateIndex(
            name: "IX_Performances_Status_StartsAt",
            table: "Performances",
            columns: new[] { "Status", "StartsAt" });

        migrationBuilder.AddCheckConstraint(
            name: "CK_Performances_AvailableSeats_Range",
            table: "Performances",
            sql: "\"AvailableSeats\" >= 0 AND \"AvailableSeats\" <= \"Capacity\"");

        migrationBuilder.AddCheckConstraint(
            name: "CK_Performances_Capacity_Positive",
            table: "Performances",
            sql: "\"Capacity\" > 0");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Performances_Status_StartsAt",
            table: "Performances");

        migrationBuilder.DropCheckConstraint(
            name: "CK_Performances_AvailableSeats_Range",
            table: "Performances");

        migrationBuilder.DropCheckConstraint(
            name: "CK_Performances_Capacity_Positive",
            table: "Performances");

        migrationBuilder.DropColumn(
            name: "CancellationReason",
            table: "Performances");

        migrationBuilder.DropColumn(
            name: "CancelledAt",
            table: "Performances");

        migrationBuilder.DropColumn(
            name: "Capacity",
            table: "Performances");

        migrationBuilder.DropColumn(
            name: "CreatedAt",
            table: "Performances");

        migrationBuilder.DropColumn(
            name: "PublishedAt",
            table: "Performances");

        migrationBuilder.DropColumn(
            name: "Status",
            table: "Performances");

        migrationBuilder.DropColumn(
            name: "UpdatedAt",
            table: "Performances");
    }
}
