using RedisCourse.Api.Models;

namespace RedisCourse.Api.Repositories;

public class FakeProductRepository
{
    private readonly Dictionary<int, Product> _products = new()
    {
        [42] = new Product
        {
            Id = 42,
            Name = "RTX 5070 Ti",
            Price = 350000
        }
    };

    public async Task<Product?> GetByIdAsync(int id)
    {
        Console.WriteLine($"DB QUERY START - Product {id}");

        await Task.Delay(2000);

        Console.WriteLine($"DB QUERY END - Product {id}");

        return _products.GetValueOrDefault(id);
    }

    public void Update(Product product)
    {
        _products[product.Id] = product;
    }
}