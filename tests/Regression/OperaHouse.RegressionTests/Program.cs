using PerformanceEntity = OperaHouse.Performance.Models.Performance;
using OperaHouse.Performance.Models;
using OperaHouse.RegressionTests;

if (args is ["--cancellation-db"])
{
    await CancellationDatabaseTests.RunAsync();
    Console.WriteLine($"All {Check.Passed} cancellation database checks passed.");
    return;
}

await NotificationTests.RunAsync();
await WorkerTests.RunAsync();

if (args is ["--notifications"])
{
    Console.WriteLine($"All {Check.Passed} notification regression checks passed.");
    return;
}

ModelCompatibilityTests.Run();
await FeatureTests.RunAsync();

var start = new DateTimeOffset(2027, 1, 10, 20, 0, 0, TimeSpan.FromHours(1));
var performance = new PerformanceEntity("Opera", "Hall", start, 10, DateTimeOffset.UtcNow);
Check.That(performance.StartsAt.Offset == TimeSpan.Zero && performance.StartsAt == start,
    "Performance creation preserves the instant in UTC");
var updated = start.AddDays(1).ToOffset(TimeSpan.FromHours(-5));
Check.That(performance.UpdateDraft("Opera", "Hall", updated, 10, DateTimeOffset.UtcNow)
    && performance.StartsAt.Offset == TimeSpan.Zero && performance.StartsAt == updated,
    "Performance update preserves the instant in UTC");

Console.WriteLine($"All {Check.Passed} regression checks passed.");

internal static class Check
{
    public static int Passed { get; private set; }

    public static void That(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        Passed++;
        Console.WriteLine($"PASS: {description}");
    }

    public static async Task<T> ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action().WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (T exception) { return exception; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
