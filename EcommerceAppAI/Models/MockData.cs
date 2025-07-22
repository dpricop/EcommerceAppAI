namespace EcommerceAppAI.Models;

public static class MockData
{
    public static List<Product> GetMockProducts()
    {
        return new List<Product>
        {
            new Product 
            { 
                Id = 1, 
                Name = "iPhone 15 Pro", 
                Price = 999.99m, 
                Category = "Electronics",
                Description = "Latest iPhone with Pro features and advanced camera system"
            },
            new Product 
            { 
                Id = 2, 
                Name = "MacBook Air M2", 
                Price = 1199.00m, 
                Category = "Electronics",
                Description = "Lightweight laptop with M2 chip and all-day battery life"
            },
            new Product 
            { 
                Id = 3, 
                Name = "AirPods Pro", 
                Price = 249.00m, 
                Category = "Electronics",
                Description = "Wireless earbuds with active noise cancellation"
            },
            new Product 
            { 
                Id = 4, 
                Name = "Nike Air Jordan", 
                Price = 180.00m, 
                Category = "Footwear",
                Description = "Classic basketball sneakers with iconic design"
            },
            new Product 
            { 
                Id = 5, 
                Name = "Levi's 501 Jeans", 
                Price = 89.99m, 
                Category = "Clothing",
                Description = "Original straight fit denim jeans in classic blue"
            },
            new Product 
            { 
                Id = 6, 
                Name = "Samsung Galaxy S24", 
                Price = 899.99m, 
                Category = "Electronics",
                Description = "Android smartphone with AI-powered camera and display"
            },
            new Product 
            { 
                Id = 7, 
                Name = "Sony WH-1000XM4", 
                Price = 349.99m, 
                Category = "Electronics",
                Description = "Premium noise-canceling over-ear headphones"
            },
            new Product 
            { 
                Id = 8, 
                Name = "Adidas Ultraboost", 
                Price = 160.00m, 
                Category = "Footwear",
                Description = "High-performance running shoes with responsive cushioning"
            },
            new Product 
            { 
                Id = 9, 
                Name = "The North Face Jacket", 
                Price = 299.00m, 
                Category = "Clothing",
                Description = "Waterproof outdoor jacket for all weather conditions"
            },
            new Product 
            { 
                Id = 10, 
                Name = "iPad Pro 12.9", 
                Price = 1099.00m, 
                Category = "Electronics",
                Description = "Professional tablet with M2 chip and Liquid Retina display"
            }
        };
    }
}