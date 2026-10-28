using Microsoft.AspNetCore.Mvc;
using RedisCourse.Api.Events;
using RedisCourse.Api.Models;
using RedisCourse.Api.Repositories;
using StackExchange.Redis;
using System.Text.Json;

namespace RedisCourse.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly IDatabase _redisDatabase;
    private readonly FakeProductRepository _repository;
    private readonly ISubscriber _subscriber;

    public ProductsController(
     IConnectionMultiplexer redis,
     FakeProductRepository repository)
    {
        _redisDatabase = redis.GetDatabase();
        _subscriber = redis.GetSubscriber();
        _repository = repository;
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var cacheKey = $"product:{id}";
        const string NotFoundCacheValue = "__NOT_FOUND__";

        var redisAvailable = true;

        RedisValue cachedProduct = RedisValue.Null;

        try
        {
            cachedProduct = await _redisDatabase.StringGetAsync(cacheKey);
        }
        catch (RedisConnectionException ex)
        {
            redisAvailable = false;

            Console.WriteLine(
                $"REDIS CONNECTION ERROR - Falling back to database: {ex.Message}");
        }
        catch (RedisTimeoutException ex)
        {
            redisAvailable = false;

            Console.WriteLine(
                $"REDIS TIMEOUT - Falling back to database: {ex.Message}");
        }

        if (!cachedProduct.IsNull)
        {
            if (cachedProduct == NotFoundCacheValue)
            {
                Console.WriteLine($"NEGATIVE CACHE HIT - Product {id}");

                return NotFound();
            }

            var productFromCache =
                JsonSerializer.Deserialize<Product>(cachedProduct.ToString());

            return Ok(new
            {
                Source = "Redis",
                Product = productFromCache
            });
        }

        var lockKey = $"lock:product:{id}";
        var lockValue = Guid.NewGuid().ToString();

        var lockAcquired = false;

        if (redisAvailable)
        {
            lockAcquired = await _redisDatabase.LockTakeAsync(
                lockKey,
                lockValue,
                TimeSpan.FromSeconds(5));

            Console.WriteLine(
                $"LOCK {(lockAcquired ? "ACQUIRED" : "FAILED")} - {lockValue}");
        }
        else
        {
            Console.WriteLine(
                $"REDIS UNAVAILABLE - Skipping distributed lock for Product {id}");
        }

        if (redisAvailable && !lockAcquired)
        {
            for (var attempt = 1; attempt <= 15; attempt++)
            {
                await Task.Delay(200);

                Console.WriteLine($"WAITING FOR CACHE - attempt {attempt}");

                var cachedAfterWait =
                    await _redisDatabase.StringGetAsync(cacheKey);

                if (!cachedAfterWait.IsNull)
                {
                    if (cachedAfterWait == NotFoundCacheValue)
                    {
                        Console.WriteLine(
                            $"NEGATIVE CACHE HIT AFTER WAIT - Product {id}");

                        return NotFound();
                    }

                    var productFromCache =
                        JsonSerializer.Deserialize<Product>(
                            cachedAfterWait.ToString());

                    return Ok(new
                    {
                        Source = "Redis after wait",
                        Product = productFromCache
                    });
                }

                // A cache még üres.
                // Megpróbáljuk megszerezni a lockot.
                lockAcquired = await _redisDatabase.LockTakeAsync(
                    lockKey,
                    lockValue,
                    TimeSpan.FromSeconds(5));

                if (lockAcquired)
                {
                    Console.WriteLine(
                        $"LOCK ACQUIRED AFTER WAIT - {lockValue}");

                    break;
                }
            }

            if (!lockAcquired)
            {
                Console.WriteLine(
                    $"LOCK NOT ACQUIRED AFTER RETRIES - {lockValue}");

                return StatusCode(
                    StatusCodes.Status503ServiceUnavailable,
                    "Could not acquire lock.");
            }
        }

        try
        {
            // Egyelőre ez szimulálja az adatbázist.
            var productFromDatabase = await _repository.GetByIdAsync(id);

            if (productFromDatabase is null)
            {
                if (redisAvailable)
                {
                    try
                    {
                        await _redisDatabase.StringSetAsync(
                            cacheKey,
                            NotFoundCacheValue,
                            TimeSpan.FromSeconds(30));

                        Console.WriteLine($"NEGATIVE CACHE SET - Product {id}");
                    }
                    catch (RedisConnectionException ex)
                    {
                        Console.WriteLine(
                            $"REDIS NEGATIVE CACHE WRITE FAILED - Ignoring: {ex.Message}");
                    }
                    catch (RedisTimeoutException ex)
                    {
                        Console.WriteLine(
                            $"REDIS NEGATIVE CACHE WRITE TIMEOUT - Ignoring: {ex.Message}");
                    }
                }

                return NotFound();
            }

            var json = JsonSerializer.Serialize(productFromDatabase);

            var cacheTtl = TimeSpan.FromMinutes(5)
               + TimeSpan.FromSeconds(Random.Shared.Next(0, 61));

            if (redisAvailable)
            {
                try
                {
                    await _redisDatabase.StringSetAsync(
                        cacheKey,
                        json,
                        cacheTtl);
                }
                catch (RedisConnectionException ex)
                {
                    Console.WriteLine(
                        $"REDIS CACHE WRITE FAILED - Ignoring: {ex.Message}");
                }
                catch (RedisTimeoutException ex)
                {
                    Console.WriteLine(
                        $"REDIS CACHE WRITE TIMEOUT - Ignoring: {ex.Message}");
                }
            }

            return Ok(new
            {
                Source = "Database",
                Product = productFromDatabase
            });
        }
        finally
        {
            if (lockAcquired)
            {
                try
                {
                    await _redisDatabase.LockReleaseAsync(
                        lockKey,
                        lockValue);

                    Console.WriteLine($"LOCK RELEASED - {lockValue}");
                }
                catch (RedisConnectionException ex)
                {
                    Console.WriteLine(
                        $"REDIS LOCK RELEASE FAILED - Lock will expire by TTL: {ex.Message}");
                }
                catch (RedisTimeoutException ex)
                {
                    Console.WriteLine(
                        $"REDIS LOCK RELEASE TIMEOUT - Lock will expire by TTL: {ex.Message}");
                }
            }
        }
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, Product product)
    {
        product.Id = id;

        // 1. Database update
        _repository.Update(product);

        // 2. Cache invalidation
        await _redisDatabase.KeyDeleteAsync($"product:{id}");

        var productUpdatedEvent = new ProductUpdatedEvent
        {
            ProductId = id,
            EventType = "ProductUpdated",
            Price = product.Price
        };

        var eventJson = JsonSerializer.Serialize(productUpdatedEvent);

        await _subscriber.PublishAsync(
            RedisChannel.Literal("product-updates"),
            eventJson);

        return NoContent();
    }
}