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
}