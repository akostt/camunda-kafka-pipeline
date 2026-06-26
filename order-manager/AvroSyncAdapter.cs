using Confluent.Kafka;

namespace OrderManager;

internal sealed class SyncDeserializerAdapter<T> : IDeserializer<T>
{
    private readonly IAsyncDeserializer<T> _inner;
    public SyncDeserializerAdapter(IAsyncDeserializer<T> inner) => _inner = inner;

    public T Deserialize(ReadOnlySpan<byte> data, bool isNull, SerializationContext context)
        => _inner.DeserializeAsync(data.ToArray(), isNull, context).GetAwaiter().GetResult();
}
