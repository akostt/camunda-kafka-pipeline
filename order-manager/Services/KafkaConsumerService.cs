using Avro.Generic;
using Confluent.Kafka;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;
using Microsoft.AspNetCore.SignalR;
using OrderManager.Hubs;

namespace OrderManager.Services;

public class KafkaConsumerService : BackgroundService
{
    private readonly IConfiguration _cfg;
    private readonly OrderStateService _state;
    private readonly IHubContext<OrderHub> _hub;
    private readonly ILogger<KafkaConsumerService> _log;

    public KafkaConsumerService(
        IConfiguration cfg,
        OrderStateService state,
        IHubContext<OrderHub> hub,
        ILogger<KafkaConsumerService> log)
    {
        _cfg = cfg; _state = state; _hub = hub; _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Yield(); // release host startup thread before blocking Consume() loop

        var bootstrapServers = _cfg["Kafka:BootstrapServers"]
            ?? throw new InvalidOperationException("Kafka:BootstrapServers не задан в конфигурации.");
        var topic     = _cfg["Kafka:Topic"]
            ?? throw new InvalidOperationException("Kafka:Topic не задан в конфигурации.");
        var group     = _cfg["Kafka:ConsumerGroup"]
            ?? throw new InvalidOperationException("Kafka:ConsumerGroup не задан в конфигурации.");
        var schemaUrl = _cfg["SchemaRegistry:Url"]
            ?? throw new InvalidOperationException("SchemaRegistry:Url не задан в конфигурации.");

        using var registry = new CachedSchemaRegistryClient(
            new SchemaRegistryConfig { Url = schemaUrl });

        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers  = bootstrapServers,
            GroupId           = group,
            AutoOffsetReset   = AutoOffsetReset.Earliest,
            EnableAutoCommit  = false,
            SessionTimeoutMs  = 30000,
            MaxPollIntervalMs = 300000,
        };

        using var consumer = new ConsumerBuilder<string?, GenericRecord>(consumerConfig)
            .SetValueDeserializer(new SyncDeserializerAdapter<GenericRecord>(new AvroDeserializer<GenericRecord>(registry)))
            .SetErrorHandler((_, e) => _log.LogError("Kafka error: {Reason}", e.Reason))
            .Build();

        consumer.Subscribe(topic);
        _log.LogInformation("Subscribed to {Topic}, replaying from earliest…", topic);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                ConsumeResult<string?, GenericRecord>? result = null;
                try
                {
                    result = consumer.Consume(TimeSpan.FromSeconds(1));
                }
                catch (ConsumeException ex)
                {
                    _log.LogWarning("Consume error: {Reason}", ex.Error.Reason);
                    await Task.Delay(1000, ct);
                    continue;
                }

                if (result?.Message?.Value == null) continue;

                var msg = AvroMapper.ToOrderMessage(result.Message.Value);
                if (msg == null)
                {
                    _log.LogWarning("Не удалось десериализовать сообщение из топика {Topic}, offset {Offset}",
                        topic, result.TopicPartitionOffset);
                    continue;
                }

                var order = msg.MessageObject;
                _state.Upsert(order);

                var listItem = OrderStateService.ToListItem(order);
                await OrderHubExtensions.NotifyOrderUpdated(_hub, listItem);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            consumer.Close();
        }
    }
}
