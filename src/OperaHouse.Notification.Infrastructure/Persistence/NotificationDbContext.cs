using Microsoft.EntityFrameworkCore;
using OperaHouse.Notification.Domain.Inbox;
using OperaHouse.Notification.Domain.Notifications;

namespace OperaHouse.Notification.Infrastructure.Persistence;

public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options)
    : DbContext(options)
{
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    public DbSet<NotificationMessage> NotificationMessages => Set<NotificationMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InboxMessage>(entity =>
        {
            entity.ToTable("notification_inbox_messages");

            entity.HasKey(inboxMessage => inboxMessage.Id);

            entity.Property(inboxMessage => inboxMessage.MessageType)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(inboxMessage => inboxMessage.Consumer)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(inboxMessage => inboxMessage.Status)
                .HasConversion<string>()
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(inboxMessage => inboxMessage.LastError)
                .HasMaxLength(2_000);

            entity.HasIndex(inboxMessage => new
                {
                    inboxMessage.MessageId,
                    inboxMessage.Consumer
                })
                .IsUnique();
        });

        modelBuilder.Entity<NotificationMessage>(entity =>
        {
            entity.ToTable("notification_messages");

            entity.HasKey(notification => notification.Id);

            entity.Property(notification => notification.Type)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(notification => notification.Recipient)
                .HasMaxLength(320)
                .IsRequired();

            entity.Property(notification => notification.Subject)
                .HasMaxLength(300)
                .IsRequired();

            entity.Property(notification => notification.Body)
                .HasMaxLength(4_000)
                .IsRequired();

            entity.Property(notification => notification.Status)
                .HasConversion<string>()
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(notification => notification.FailureReason)
                .HasMaxLength(2_000);

            entity.HasIndex(notification => notification.InboxMessageId)
                .IsUnique();
        });
    }
}
