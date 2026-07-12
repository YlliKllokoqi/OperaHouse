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
        SeedPerformances(modelBuilder);
    }

    private static void ConfigureBookings(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Domain.Bookings.Booking>(booking =>
        {
            booking.ToTable("Bookings");

            booking.HasKey(x => x.Id);

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
            performance.ToTable("Performances");

            performance.HasKey(x => x.Id);

            performance.Property(x => x.Title)
                .HasMaxLength(200)
                .IsRequired();

            performance.Property(x => x.Venue)
                .HasMaxLength(200)
                .IsRequired();

            performance.Property(x => x.StartsAt)
                .IsRequired();

            performance.Property(x => x.AvailableSeats)
                .IsRequired();
        });
    }

    private static void SeedPerformances(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Performance>().HasData(
            new Performance
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Title = "La Traviata",
                Venue = "OperaHouse Main Hall",
                StartsAt = new DateTimeOffset(
                    2027, 10, 10, 19, 0, 0, TimeSpan.Zero),
                AvailableSeats = 500
            },
            new Performance
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Title = "The Magic Flute",
                Venue = "OperaHouse Main Hall",
                StartsAt = new DateTimeOffset(
                    2027, 11, 15, 19, 30, 0, TimeSpan.Zero),
                AvailableSeats = 450
            },
            new Performance
            {
                Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                Title = "Beethoven Symphony No. 9",
                Venue = "OperaHouse Concert Hall",
                StartsAt = new DateTimeOffset(
                    2027, 12, 5, 20, 0, 0, TimeSpan.Zero),
                AvailableSeats = 600
            });
    }
}