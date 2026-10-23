using RedisCourse.Api.Events;
using StackExchange.Redis;
using System.Text.Json;

namespace RedisCourse.Api.Services;

public class ProductUpdateSubscriber : BackgroundService
{
    private readonly IConnectionMultiplexer _redis;

    public ProductUpdateSubscriber(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        var subscriber = _redis.GetSubscriber();

        await subscriber.SubscribeAsync(
            RedisChannel.Literal("product-updates"),
            (channel, message) =>
            {
                try
                {
                    var productUpdatedEvent =
                        JsonSerializer.Deserialize<ProductUpdatedEvent>(
                            message.ToString());

                    if (productUpdatedEvent is null)
                    {
                        return;
                    }

                    Console.WriteLine(
                        $".NET SUBSCRIBER RECEIVED - " +
                        $"ProductId: {productUpdatedEvent.ProductId}, " +
                        $"EventType: {productUpdatedEvent.EventType}, " +
                        $"Price: {productUpdatedEvent.Price}");
                }
                catch (JsonException ex)
                {
                    Console.WriteLine(
                        $"INVALID MESSAGE: {message}");

                    Console.WriteLine(
                        $"JSON ERROR: {ex.Message}");
                }
            });

        await Task.Delay(
            Timeout.Infinite,
            stoppingToken);
    }
}