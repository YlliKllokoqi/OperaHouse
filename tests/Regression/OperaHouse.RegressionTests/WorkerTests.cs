using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OperaHouse.Messaging;
using OperaHouse.Notification.Email;
using OperaHouse.Notification.Features;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using NotificationWorker = OperaHouse.Notification.Messaging.Worker;

namespace OperaHouse.RegressionTests;

internal static class WorkerTests
{
    public static async Task RunAsync()
    {
        await CheckTopologyAsync();

        foreach (var created in new[] { true, false })
        {
            var eventName = created ? "BookingCreated" : "BookingExpired";
            foreach (var json in new[] { "{}", "null", "{broken" })
            {
                using var fixture = new Session();
                var session = fixture.Worker.RunAsync(fixture.Cancellation.Token);
                await fixture.DeliverAsync(created, json);
                var warnings = fixture.Logger.Entries.Where(x => x.Level >= LogLevel.Warning).ToArray();
                Check.That(fixture.Channel.Nacks == 1 && !fixture.Channel.Requeue
                    && fixture.Channel.Acks == 0 && fixture.RetryChannel.Publishes == 0
                    && fixture.Sender.Sends == 0 && warnings.Length == 1
                    && warnings[0].Level == LogLevel.Warning && warnings[0].Exception is not null,
                    $"{eventName}: malformed delivery {json} goes directly to dead-lettering");
                fixture.Cancellation.Cancel();
                await Check.ThrowsAsync<OperationCanceledException>(() => session);
            }

            using (var fixture = new Session())
            {
                var cause = new InvalidOperationException("Mailbox rejected");
                fixture.Sender.Failure = new PermanentEmailDeliveryException(cause);
                var session = fixture.Worker.RunAsync(fixture.Cancellation.Token);
                var json = created ? JsonSerializer.Serialize(NotificationTests.Created())
                    : JsonSerializer.Serialize(NotificationTests.Expired());
                await fixture.DeliverAsync(created, json);
                using var payload = JsonDocument.Parse(json);
                var errors = fixture.Logger.Entries.Where(x => x.Level >= LogLevel.Warning).ToArray();
                Check.That(fixture.Sender.Sends == 1 && fixture.Channel.Nacks == 1 && !fixture.Channel.Requeue
                    && fixture.Channel.Acks == 0 && fixture.RetryChannel.Publishes == 0
                    && errors.Length == 1 && errors[0].Level == LogLevel.Error
                    && errors[0].Exception?.InnerException == cause
                    && Equals(errors[0].Context["MessageId"], payload.RootElement.GetProperty("MessageId").GetGuid())
                    && Equals(errors[0].Context["CorrelationId"], payload.RootElement.GetProperty("CorrelationId").GetGuid()),
                    $"{eventName}: permanent email rejection goes directly to dead-lettering with its cause and IDs");
                fixture.Cancellation.Cancel();
                await Check.ThrowsAsync<OperationCanceledException>(() => session);
            }

            using (var fixture = new Session())
            {
                fixture.Sender.Failure = new IOException("Email server unavailable");
                fixture.RetryChannel.PublishFailure = new IOException("Retry publication failed");
                var session = fixture.Worker.RunAsync(fixture.Cancellation.Token);
                var json = created ? JsonSerializer.Serialize(NotificationTests.Created())
                    : JsonSerializer.Serialize(NotificationTests.Expired());
                var error = await Check.ThrowsAsync<IOException>(() => fixture.DeliverAsync(created, json));
                // RabbitMQ's dispatcher reports escaped delivery errors through this event.
                await fixture.Channel.CallbackExceptionAsync!(fixture.Channel,
                    new CallbackExceptionEventArgs(new Dictionary<string, object>(), error));
                await Check.ThrowsAsync<IOException>(() => session);
                Check.That(fixture.Channel.Acks == 0 && fixture.Channel.Nacks == 0
                    && fixture.RetryChannel.Publishes == 1 && fixture.Channel.Disposed
                    && fixture.RetryChannel.Disposed,
                    $"{eventName}: failed retry ends the session without acknowledging the original");
            }

            using (var fixture = new Session())
            {
                fixture.Sender.Failure = new IOException("Email server unavailable");
                var session = fixture.Worker.RunAsync(fixture.Cancellation.Token);
                var json = created ? JsonSerializer.Serialize(NotificationTests.Created())
                    : JsonSerializer.Serialize(NotificationTests.Expired());
                await fixture.DeliverAsync(created, json);
                var warnings = fixture.Logger.Entries.Where(x => x.Level >= LogLevel.Warning).ToArray();
                var messageId = JsonDocument.Parse(json).RootElement.GetProperty("MessageId").GetGuid();
                Check.That(fixture.Channel.Acks == 1 && fixture.RetryChannel.Publishes == 1
                    && fixture.RetryChannel.LastRoutingKey == (created
                        ? "booking.created.retry" : "booking.expired.retry")
                    && warnings.Length == 1 && warnings[0].Level == LogLevel.Warning
                    && warnings[0].Exception == fixture.Sender.Failure
                    && Equals(warnings[0].Context["MessageId"], messageId)
                    && warnings[0].Context.ContainsKey("CorrelationId")
                    && warnings[0].Context.ContainsKey("BookingId"),
                    $"{eventName}: retry acknowledges and logs its exception and identifiers once");
                fixture.Cancellation.Cancel();
                await Check.ThrowsAsync<OperationCanceledException>(() => session);
            }

            using (var fixture = new Session())
            {
                fixture.Sender.Failure = new IOException("Email server unavailable");
                var session = fixture.Worker.RunAsync(fixture.Cancellation.Token);
                var json = created ? JsonSerializer.Serialize(NotificationTests.Created())
                    : JsonSerializer.Serialize(NotificationTests.Expired());
                await fixture.DeliverAsync(created, json, retryCount: 3);
                var failures = fixture.Logger.Entries.Where(x => x.Level >= LogLevel.Warning).ToArray();
                Check.That(fixture.Channel.Nacks == 1 && !fixture.Channel.Requeue
                    && fixture.RetryChannel.Publishes == 0 && failures.Length == 1
                    && failures[0].Level == LogLevel.Error && failures[0].Exception == fixture.Sender.Failure,
                    $"{eventName}: exhausted retries produce one error with the original exception");
                fixture.Cancellation.Cancel();
                await Check.ThrowsAsync<OperationCanceledException>(() => session);
            }

            using (var fixture = new Session())
            {
                var session = fixture.Worker.RunAsync(fixture.Cancellation.Token);
                var json = created ? JsonSerializer.Serialize(NotificationTests.Created())
                    : JsonSerializer.Serialize(NotificationTests.Expired());
                await fixture.DeliverAsync(created, json);
                await fixture.DeliverAsync(created, json);
                Check.That(fixture.Channel.Acks == 2 && fixture.Sender.Sends == 1
                    && fixture.Logger.Entries.Count == 3
                    && fixture.Logger.Entries.All(x => x.Level == LogLevel.Information),
                    $"{eventName}: startup and two delivery outcomes produce only three logs");
                fixture.Cancellation.Cancel();
                await Check.ThrowsAsync<OperationCanceledException>(() => session);
            }
        }

        using (var fixture = new Session())
        {
            var session = fixture.Worker.RunAsync(fixture.Cancellation.Token);
            await fixture.RetryChannel.ChannelShutdownAsync!(fixture.RetryChannel,
                new ShutdownEventArgs(ShutdownInitiator.Peer, 404, "Retry exchange missing"));
            await Check.ThrowsAsync<IOException>(() => session);
            Check.That(fixture.Channel.Disposed, "Closed retry channel ends the session for reconnection");
        }
    }

    private static async Task CheckTopologyAsync()
    {
        var options = new RabbitMqOptions
        {
            ExchangeName = "test.events",
            RetryExchangeName = "test.retry",
            DeadLetterExchangeName = "test.dead",
            BookingCreatedQueueName = "created.main",
            BookingCreatedRoutingKey = "created.key",
            BookingCreatedRetryQueueName = "created.retry",
            BookingCreatedRetryRoutingKey = "created.retry.key",
            BookingCreatedRetryDelayMilliseconds = 5001,
            BookingCreatedDeadLetterQueueName = "created.dead",
            BookingCreatedDeadLetterRoutingKey = "created.dead.key",
            BookingExpiredQueueName = "expired.main",
            BookingExpiredRoutingKey = "expired.key",
            BookingExpiredRetryQueueName = "expired.retry",
            BookingExpiredRetryRoutingKey = "expired.retry.key",
            BookingExpiredRetryDelayMilliseconds = 7003,
            BookingExpiredDeadLetterQueueName = "expired.dead",
            BookingExpiredDeadLetterRoutingKey = "expired.dead.key"
        };
        using var fixture = new Session(options);
        var session = fixture.Worker.RunAsync(fixture.Cancellation.Token);
        await fixture.Channel.Ready.Task.WaitAsync(fixture.Cancellation.Token);

        Check.That(fixture.Channel.Exchanges.Count == 3
            && fixture.Channel.Exchanges.Keys.Order().SequenceEqual(new[] { "test.dead", "test.events", "test.retry" })
            && fixture.Channel.Exchanges.Values.All(x => x == ("topic", true, false)),
            "Notification exchanges retain their configured names, topic type, and durability");
        Check.That(fixture.Channel.Queues.Count == 6 && fixture.Channel.Bindings.Count == 6
            && fixture.Channel.Queues.Values.All(x => x.Durable && !x.Exclusive && !x.AutoDelete),
            "All six notification queues remain durable, shared, and non-auto-deleting");

        foreach (var (kind, delay) in new[] { ("created", 5001), ("expired", 7003) })
        {
            var main = fixture.Channel.Queues[$"{kind}.main"].Arguments;
            var retry = fixture.Channel.Queues[$"{kind}.retry"].Arguments;
            var dead = fixture.Channel.Queues[$"{kind}.dead"].Arguments;
            Check.That(main.Count == 2 && Equals(main["x-dead-letter-exchange"], "test.dead")
                && Equals(main["x-dead-letter-routing-key"], $"{kind}.dead.key")
                && retry.Count == 3 && Equals(retry["x-message-ttl"], delay)
                && Equals(retry["x-dead-letter-exchange"], "test.events")
                && Equals(retry["x-dead-letter-routing-key"], $"{kind}.key") && dead.Count == 0
                && fixture.Channel.Bindings.Contains(($"{kind}.main", "test.events", $"{kind}.key"))
                && fixture.Channel.Bindings.Contains(($"{kind}.retry", "test.retry", $"{kind}.retry.key"))
                && fixture.Channel.Bindings.Contains(($"{kind}.dead", "test.dead", $"{kind}.dead.key")),
                $"{kind}: configured main/retry/dead-letter bindings and independent retry TTL are preserved");
        }

        fixture.Cancellation.Cancel();
        await Check.ThrowsAsync<OperationCanceledException>(() => session);
    }

    private sealed class Session : IDisposable
    {
        private readonly ServiceProvider _services;
        private readonly IChannel _channel;
        public ChannelStub Channel { get; }
        public ChannelStub RetryChannel { get; }
        public EmailSender Sender { get; } = new();
        public RecordingLogger<NotificationWorker> Logger { get; } = new();
        public CancellationTokenSource Cancellation { get; } = new(TimeSpan.FromSeconds(10));
        public TestWorker Worker { get; }

        public Session(RabbitMqOptions? options = null)
        {
            _channel = DispatchProxy.Create<IChannel, ChannelStub>();
            var retry = DispatchProxy.Create<IChannel, ChannelStub>();
            Channel = (ChannelStub)_channel;
            RetryChannel = (ChannelStub)retry;
            var channels = new Queue<IChannel>([_channel, retry]);
            var connection = InterfaceStub.Create<IConnection>((method, _) => method.Name switch
            {
                "CreateChannelAsync" => Task.FromResult(channels.Dequeue()),
                "DisposeAsync" => ValueTask.CompletedTask,
                _ when method.Name.StartsWith("add_") || method.Name.StartsWith("remove_") => null,
                _ => throw new NotSupportedException(method.Name)
            });
            var factory = InterfaceStub.Create<IConnectionFactory>((method, _) => method.Name == "CreateConnectionAsync"
                ? Task.FromResult(connection) : throw new NotSupportedException(method.Name));
            var store = new MemoryStore();
            _services = new ServiceCollection()
                .AddScoped(_ => new SendBookingConfirmation(store.Processor(Sender)))
                .AddScoped(_ => new SendBookingExpiration(store.Processor(Sender)))
                .BuildServiceProvider();
            Worker = new TestWorker(_services.GetRequiredService<IServiceScopeFactory>(), factory, Logger,
                options ?? new RabbitMqOptions());
        }

        public async Task DeliverAsync(bool created, string json, int retryCount = 0)
        {
            await Channel.Ready.Task.WaitAsync(Cancellation.Token);
            var consumer = Channel.Consumers[created ? 0 : 1];
            await consumer.HandleBasicDeliverAsync("test", 1, false, "operahouse.events",
                created ? "booking.created" : "booking.expired", new BasicProperties
                { Headers = new Dictionary<string, object?> { ["x-retry-count"] = retryCount } },
                Encoding.UTF8.GetBytes(json), Cancellation.Token);
        }

        public void Dispose()
        {
            Cancellation.Cancel();
            Worker.Dispose();
            Cancellation.Dispose();
            _services.Dispose();
        }
    }

    private sealed class TestWorker(IServiceScopeFactory scopes, IConnectionFactory factory,
        ILogger<NotificationWorker> logger, RabbitMqOptions options)
        : NotificationWorker(logger,
            Options.Create(options), scopes, factory)
    {
        public Task RunAsync(CancellationToken token) => RunSessionAsync(token);
    }
}

internal sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly LoggerExternalScopeProvider _scopes = new();
    public List<(LogLevel Level, Exception? Exception, Dictionary<string, object?> Context)> Entries { get; } = [];
    public bool IsEnabled(LogLevel level) => true;
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => _scopes.Push(state);
    public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var context = new Dictionary<string, object?>();
        _scopes.ForEachScope((scope, values) =>
        {
            if (scope is IEnumerable<KeyValuePair<string, object?>> pairs)
                foreach (var pair in pairs) values[pair.Key] = pair.Value;
        }, context);
        if (state is IEnumerable<KeyValuePair<string, object?>> fields)
            foreach (var field in fields) context[field.Key] = field.Value;
        Entries.Add((level, exception, context));
    }
}

public class ChannelStub : DispatchProxy
{
    public List<AsyncEventingBasicConsumer> Consumers { get; } = [];
    public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public AsyncEventHandler<CallbackExceptionEventArgs>? CallbackExceptionAsync;
    public AsyncEventHandler<ShutdownEventArgs>? ChannelShutdownAsync;
    public Exception? PublishFailure;
    public int Acks, Nacks, Publishes;
    public bool Requeue, Disposed;
    public string? LastRoutingKey;
    public Dictionary<string, (string Type, bool Durable, bool AutoDelete)> Exchanges { get; } = [];
    public Dictionary<string, (bool Durable, bool Exclusive, bool AutoDelete, Dictionary<string, object?> Arguments)> Queues { get; } = [];
    public List<(string Queue, string Exchange, string RoutingKey)> Bindings { get; } = [];

    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        var name = method!.Name;
        switch (name)
        {
            case "add_CallbackExceptionAsync": CallbackExceptionAsync += (AsyncEventHandler<CallbackExceptionEventArgs>)args![0]!; return null;
            case "add_ChannelShutdownAsync": ChannelShutdownAsync += (AsyncEventHandler<ShutdownEventArgs>)args![0]!; return null;
            case "BasicConsumeAsync":
                Consumers.Add(args!.OfType<AsyncEventingBasicConsumer>().Single());
                if (Consumers.Count == 2) Ready.TrySetResult();
                return Task.FromResult("test");
            case "BasicPublishAsync":
                Publishes++; LastRoutingKey = (string)args![1]!;
                return PublishFailure is null ? ValueTask.CompletedTask : ValueTask.FromException(PublishFailure);
            case "BasicAckAsync": Acks++; return ValueTask.CompletedTask;
            case "BasicNackAsync": Nacks++; Requeue = (bool)args![2]!; return ValueTask.CompletedTask;
            case "DisposeAsync": Disposed = true; return ValueTask.CompletedTask;
            case "QueueDeclareAsync":
                var arguments = args![4] is IDictionary<string, object?> values
                    ? new Dictionary<string, object?>(values) : new Dictionary<string, object?>();
                Queues.Add((string)args[0]!, ((bool)args[1]!, (bool)args[2]!, (bool)args[3]!, arguments));
                return Task.FromResult(new QueueDeclareOk((string)args[0]!, 0, 0));
            case "ExchangeDeclareAsync":
                Exchanges.Add((string)args![0]!, ((string)args[1]!, (bool)args[2]!, (bool)args[3]!));
                return Task.CompletedTask;
            case "QueueBindAsync":
                Bindings.Add(((string)args![0]!, (string)args[1]!, (string)args[2]!));
                return Task.CompletedTask;
            case "BasicQosAsync": return Task.CompletedTask;
            default: throw new NotSupportedException(name);
        }
    }
}

public class InterfaceStub : DispatchProxy
{
    private Func<MethodInfo, object?[], object?> _invoke = null!;
    public static T Create<T>(Func<MethodInfo, object?[], object?> invoke) where T : class
    {
        var instance = DispatchProxy.Create<T, InterfaceStub>();
        ((InterfaceStub)(object)instance)._invoke = invoke;
        return instance;
    }
    protected override object? Invoke(MethodInfo? method, object?[]? args) => _invoke(method!, args ?? []);
}
