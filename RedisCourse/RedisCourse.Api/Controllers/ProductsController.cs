using Microsoft.AspNetCore.Mvc;
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

    public ProductsController(    IConnectionMultiplexer redis, FakeProductRepository repository)
    {
        _redisDatabase = redis.GetDatabase();
        _repository = repository;
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var cacheKey = $"product:{id}";

        var cachedProduct = await _redisDatabase.StringGetAsync(cacheKey);

        if (!cachedProduct.IsNull)
        {
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

        var lockAcquired = await _redisDatabase.LockTakeAsync(
            lockKey,
            lockValue,
            TimeSpan.FromSeconds(5));

        Console.WriteLine(
            $"LOCK {(lockAcquired ? "ACQUIRED" : "FAILED")} - {lockValue}");

        if (!lockAcquired)
        {
            for (var attempt = 1; attempt <= 15; attempt++)
            {
                await Task.Delay(200);

                Console.WriteLine($"WAITING FOR CACHE - attempt {attempt}");

                var cachedAfterWait =
                    await _redisDatabase.StringGetAsync(cacheKey);

                if (!cachedAfterWait.IsNull)
                {
                    var productFromCache =
                        JsonSerializer.Deserialize<Product>(cachedAfterWait.ToString());

                    return Ok(new
                    {
                        Source = "Redis after wait",
                        Product = productFromCache
                    });
                }
            }
        }

        // Egyelőre ez szimulálja az adatbázist.
        var productFromDatabase = await _repository.GetByIdAsync(id);

        if (productFromDatabase is null)
        {
            return NotFound();
        }

        var json = JsonSerializer.Serialize(productFromDatabase);

        await _redisDatabase.StringSetAsync(
            cacheKey,
            json,
            TimeSpan.FromMinutes(5));

        if (lockAcquired)
        {
            await _redisDatabase.LockReleaseAsync(lockKey, lockValue);

            Console.WriteLine($"LOCK RELEASED - {lockValue}");
        }

        return Ok(new
        {
            Source = "Database",
            Product = productFromDatabase
        });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, Product product)
    {
        product.Id = id;

        // 1. Database update
        _repository.Update(product);

        // 2. Cache invalidation
        await _redisDatabase.KeyDeleteAsync($"product:{id}");

        return NoContent();
    }
}