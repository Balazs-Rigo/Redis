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
}