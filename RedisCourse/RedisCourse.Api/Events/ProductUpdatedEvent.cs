namespace RedisCourse.Api.Events;

public class ProductUpdatedEvent
{
    public int ProductId { get; set; }

    public string EventType { get; set; } = string.Empty;

    public decimal Price { get; set; }
}