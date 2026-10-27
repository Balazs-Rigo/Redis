using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;

namespace RedisCourse.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RedisController : ControllerBase
{
    private readonly IDatabase _redisDatabase;

    public RedisController(IConnectionMultiplexer redis)
    {
        _redisDatabase = redis.GetDatabase();
    }

    [HttpPost("set")]
    public async Task<IActionResult> Set()
    {
        await _redisDatabase.StringSetAsync(
            "course:user:1",
            "Balazs",
            TimeSpan.FromMinutes(5));

        return Ok();
    }

    [HttpGet("get")]
    public async Task<IActionResult> Get()
    {
        var value = await _redisDatabase.StringGetAsync("course:user:1");

        if (value.IsNull)
        {
            return NotFound();
        }

        return Ok(value.ToString());
    }

    [HttpPost("sequential")]
    public async Task<IActionResult> Sequential()
    {
        for (var i = 1; i <= 1000; i++)
        {
            await _redisDatabase.StringSetAsync(
                $"pipeline:test:{i}",
                $"value-{i}");
        }

        return Ok();
    }

    [HttpPost("pipelined")]
    public async Task<IActionResult> Pipelined()
    {
        var tasks = new List<Task>();

        for (var i = 1; i <= 1000; i++)
        {
            tasks.Add(
                _redisDatabase.StringSetAsync(
                    $"pipeline:test:{i}",
                    $"value-{i}"));
        }

        await Task.WhenAll(tasks);

        return Ok();
    }

    [HttpPost("batch")]
    public async Task<IActionResult> Batch()
    {
        var batch = _redisDatabase.CreateBatch();
        var tasks = new List<Task>();

        for (var i = 1; i <= 1000; i++)
        {
            tasks.Add(
                batch.StringSetAsync(
                    $"batch:test:{i}",
                    $"value-{i}"));
        }

        batch.Execute();

        await Task.WhenAll(tasks);

        return Ok();
    }

    [HttpPost("transaction")]
    public async Task<IActionResult> Transaction()
    {
        const string key = "account:balance";

        var currentBalance = await _redisDatabase.StringGetAsync(key);

        if (!currentBalance.TryParse(out int balance))
        {
            return BadRequest("Balance is missing or invalid.");
        }

        var newBalance = balance - 100;

        var transaction = _redisDatabase.CreateTransaction();

        transaction.AddCondition(Condition.StringEqual(key, balance));

        _ = transaction.StringSetAsync(
            key,
            newBalance);

        var success = await transaction.ExecuteAsync();

        if (!success)
        {
            return Conflict(
                "Balance changed before the transaction could be executed.");
        }

        return Ok(new
        {
            OldBalance = balance,
            NewBalance = newBalance
        });
    }

    [HttpPost("lua-transfer")]
    public async Task<IActionResult> LuaTransfer()
    {
        const string script = """
        local balance = tonumber(redis.call('GET', KEYS[1]))
        local amount = tonumber(ARGV[1])

        if balance >= amount then
            redis.call('DECRBY', KEYS[1], amount)
            redis.call('INCRBY', KEYS[2], amount)
            return 1
        else
            return 0
        end
        """;

        var result = await _redisDatabase.ScriptEvaluateAsync(
            script,
            new RedisKey[]
            {
            "lua:account:balazs",
            "lua:account:peter"
            },
            new RedisValue[]
            {
            300
            });

        return Ok(new
        {
            Success = (int)result == 1
        });
    }

    [HttpPost("json-product")]
    public async Task<IActionResult> SetJsonProduct()
    {
        const string key = "product:json:42";

        const string json = """
        {
            "id": 42,
            "name": "RTX 5070 Ti",
            "price": 350000,
            "manufacturer": {
                "name": "MSI",
                "country": "Taiwan"
            }
        }
        """;

        await _redisDatabase.ExecuteAsync(
            "JSON.SET",
            key,
            "$",
            json);

        return Ok();
    }

    [HttpGet("json-product/manufacturer")]
    public async Task<IActionResult> GetJsonProductManufacturer()
    {
        var result = await _redisDatabase.ExecuteAsync(
            "JSON.GET",
            "product:json:42",
            "$.manufacturer.name");

        return Ok(result.ToString());
    }

    [HttpPut("json-product/price/{price:int}")]
    public async Task<IActionResult> UpdateJsonProductPrice(int price)
    {
        await _redisDatabase.ExecuteAsync(
            "JSON.SET",
            "product:json:42",
            "$.price",
            price);

        return Ok(new
        {
            Price = price
        });
    }
}