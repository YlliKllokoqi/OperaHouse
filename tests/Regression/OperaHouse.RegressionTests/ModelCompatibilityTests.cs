using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using OperaHouse.Persistence;
using OperaHouse.Notification.Persistence;

namespace OperaHouse.RegressionTests;

internal static class ModelCompatibilityTests
{
    public static void Run()
    {
        const string connection = "Host=localhost;Database=model_checks;Username=unused;Password=unused";
        using var booking = new BookingDbContext(
            new DbContextOptionsBuilder<BookingDbContext>().UseNpgsql(connection).Options);
        using var notification = new NotificationDbContext(
            new DbContextOptionsBuilder<NotificationDbContext>().UseNpgsql(connection).Options);

        Verify(booking,
        [
            "20260621165117_InitialCreate",
            "20260703150704_AddOutboxMessages",
            "20260710151319_AddOutboxCorrelationId",
            "20260808220710_AddPerformanceAdministration",
            "20260814172905_AddBookingReservations"
        ]);
        Verify(notification, ["20260704124545_AddNotificationInbox"]);

        Check.That(notification.Model.GetEntityTypes().All(entity =>
                !entity.Name.StartsWith("OperaHouse.Booking.", StringComparison.Ordinal))
            && typeof(NotificationDbContext).Assembly.GetReferencedAssemblies().All(assembly =>
                assembly.Name is not ("OperaHouse.Api" or "OperaHouse.Booking"
                    or "OperaHouse.Performance" or "OperaHouse.Persistence")),
            "Notification persistence has no Booking, Performance, or shared reservation storage dependency");

        var businessAssemblies = new[]
        {
            typeof(OperaHouse.Booking.Models.Booking).Assembly,
            typeof(OperaHouse.Performance.Models.Performance).Assembly
        };
        Check.That(businessAssemblies.All(assembly => assembly.GetReferencedAssemblies().All(reference =>
                reference.Name is not ("OperaHouse.Api" or "OperaHouse.Persistence"
                    or "OperaHouse.Notification" or "OperaHouse.Booking" or "OperaHouse.Performance")
                && !reference.Name!.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal))),
            "Booking and Performance own their business code independently of the host and storage implementation");
    }

    private static void Verify(DbContext context, string[] expectedMigrations)
    {
        var name = context.GetType().Name;
        var migrations = context.GetService<IMigrationsAssembly>();
        Check.That(migrations.Migrations.Keys.Order().SequenceEqual(expectedMigrations.Order()),
            $"{name}: original migration IDs remain discoverable");
        Check.That(migrations.ModelSnapshot is not null,
            $"{name}: original model snapshot remains discoverable");

        var designModel = context.GetService<IDesignTimeModel>().Model;
        var snapshotModel = context.GetService<IModelRuntimeInitializer>()
            .Initialize(migrations.ModelSnapshot!.Model, designTime: true);
        var differences = context.GetService<IMigrationsModelDiffer>().GetDifferences(
            snapshotModel.GetRelationalModel(), designModel.GetRelationalModel());
        Check.That(differences.Count == 0,
            $"{name}: schema matches its migration snapshot ({string.Join(", ", differences.Select(operation => operation.GetType().Name))})");
        Check.That(RelationalShape(context.Model).SequenceEqual(RelationalShape(designModel)),
            $"{name}: runtime and design models map the same tables and columns");
    }

    private static IEnumerable<string> RelationalShape(IModel model) =>
        model.GetRelationalModel().Tables.OrderBy(table => table.Schema).ThenBy(table => table.Name)
            .Select(table => $"{table.Schema}.{table.Name}:" + string.Join(",",
                table.Columns.OrderBy(column => column.Name)
                    .Select(column => $"{column.Name}:{column.StoreType}:{column.IsNullable}")));
}
