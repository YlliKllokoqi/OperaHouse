using Microsoft.EntityFrameworkCore;
using OperaHouse.Booking.Domain.Bookings;
using OperaHouse.Booking.Domain.Performances;
using OperaHouse.Booking.Infrastructure.Persistence.Outbox;

namespace OperaHouse.Booking.Infrastructure.Persistence;

public sealed class BookingDbContext(
    DbContextOptions<BookingDbContext> options)
    : DbContext(options)
{
    public DbSet<Domain.Bookings.Booking> Bookings => Set<Domain.Bookings.Booking>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<Performance> Performances => Set<Performance>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureBookings(modelBuilder);
        ConfigurePerformances(modelBuilder);
    }

    private static void ConfigureBookings(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Domain.Bookings.Booking>(booking =>
        {
            booking.ToTable("Bookings",
                table =>
                {
                    table.HasCheckConstraint(
                        "CK_Bookings_Seats_Positive",
                        "\"Seats\" > 0");
                    table.HasCheckConstraint(
                        "CK_Bookings_Expiration_After_Creation",
                        "\"ExpiresAt\" > \"CreatedAt\"");
                });

            booking.HasKey(x => x.Id);

            booking.Property(x => x.IdempotencyKey)
                .IsRequired();

            booking.HasIndex(x => x.IdempotencyKey)
                .IsUnique()
                .HasDatabaseName(
                    "UX_Bookings_IdempotencyKey");

            booking.Property(x => x.CustomerEmail)
                .HasMaxLength(320)
                .IsRequired();

            booking.Property(x => x.Seats)
                .IsRequired();

            booking.Property(x => x.Status)
                .HasConversion<string>()
                .HasMaxLength(50)
                .IsRequired();

            booking.Property(x => x.CreatedAt)
                .IsRequired();

            booking.HasIndex(x => new
            {
                x.Status,
                x.ExpiresAt
            });

            booking.HasOne<Performance>()
                .WithMany()
                .HasForeignKey(x => x.PerformanceId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OutboxMessage>(outbox =>
        {
            outbox.ToTable("OutboxMessages");

            outbox.HasKey(x => x.Id);

            outbox.HasIndex(x => x.MessageId)
                .IsUnique();

            outbox.Property(x => x.CorrelationId)
                .IsRequired();

            outbox.HasIndex(x => new
            {
                x.ProcessedAt,
                x.OccurredAt
            });

            outbox.Property(x => x.Type)
                .HasMaxLength(300)
                .IsRequired();

            outbox.Property(x => x.RoutingKey)
                .HasMaxLength(200)
                .IsRequired();

            outbox.Property(x => x.Payload)
                .IsRequired();

            outbox.Property(x => x.OccurredAt)
                .IsRequired();

            outbox.Property(x => x.PublishAttempts)
                .IsRequired();

            outbox.Property(x => x.LastError)
                .HasMaxLength(2000);
        });
    }

    private static void ConfigurePerformances(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Performance>(performance =>
        {
            performance.ToTable(
                "Performances",
                table =>
                {
                    table.HasCheckConstraint(
                        "CK_Performances_Capacity_Positive",
                        "\"Capacity\" > 0");

                    table.HasCheckConstraint(
                        "CK_Performances_AvailableSeats_Range",
                        "\"AvailableSeats\" >= 0 AND "
                        + "\"AvailableSeats\" <= \"Capacity\"");
                });

            performance.HasKey(x => x.Id);

            performance.Property(x => x.Title)
                .HasMaxLength(200)
                .IsRequired();

            performance.Property(x => x.Venue)
                .HasMaxLength(200)
                .IsRequired();

            performance.Property(x => x.Status)
                .HasConversion<string>()
                .HasMaxLength(50)
                .IsRequired();

            performance.Property(x => x.StartsAt)
                .IsRequired();

            performance.Property(x => x.Capacity)
                .IsRequired();

            performance.Property(x => x.AvailableSeats)
                .IsRequired();

            performance.Property(x => x.CreatedAt)
                .IsRequired();

            performance.Property(x => x.UpdatedAt)
                .IsRequired();

            performance.Property(x => x.CancellationReason)
                .HasMaxLength(1_000);

            performance.HasIndex(x => new
            {
                x.Status,
                x.StartsAt
            });
        });
    }
}
