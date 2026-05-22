// Synthetix sample
// =================
// This small program shows what Synthetix does. It declares a "from" type
// (Order, with nested objects), a flat "to" type (OrderDto), and a mapper. The
// generator writes the body of OrderMapper.ToDto at compile time.
//
// To see the generated code after building, look under:
//   obj/Debug/net10.0/generated/Synthetix.Generator/Synthetix.SynthetixGenerator/
//
// To create or refresh the mapping manifest, run:
//   dotnet build -t:SynthetixUpdateManifest
// which writes mapping/OrderMapper.manifest.md and .json.

using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Synthetix;

namespace Synthetix.Sample;

// ----- The "from" side: a small order with nested objects -----

public sealed class Order
{
    public int Id { get; set; }
    public DateTime PlacedUtc { get; set; }
    public decimal Total { get; set; }
    public OrderStatus Status { get; set; }
    public Customer Customer { get; set; } = new();

    // A collection - deep-mapped element by element (design doc 7.6).
    public List<LineItem> Lines { get; set; } = new();
}

public sealed class LineItem
{
    public string Product { get; set; } = string.Empty;
    public int Quantity { get; set; }
}

public sealed class Customer
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public Address Address { get; set; } = new();
}

public sealed class Address
{
    public string City { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
}

public enum OrderStatus
{
    Pending,
    Shipped,
    Delivered,
}

// ----- The "to" side: a flat data-transfer object -----

public sealed class OrderDto
{
    // Mapped automatically because the name matches (Id -> Id).
    public int Id { get; set; }

    // Renamed from Order.PlacedUtc with [MapProperty].
    public DateTime OrderDate { get; set; }

    // Filled by a converter method with [MapProperty(Use = ...)].
    public string DisplayTotal { get; set; } = string.Empty;

    // Mapped enum-to-enum, by member name.
    public OrderStatusDto Status { get; set; }

    // Flattened from Order.Customer.Name by naming convention.
    public string CustomerName { get; set; } = string.Empty;

    // Flattened from Order.Customer.Email.
    public string CustomerEmail { get; set; } = string.Empty;

    // Flattened from Order.Customer.Address.City (three levels deep).
    public string CustomerAddressCity { get; set; } = string.Empty;

    // Given a fixed value with [MapValue].
    public string Source { get; set; } = string.Empty;

    // Deliberately left unset with [MapperIgnoreTarget].
    public DateTime? ExportedAt { get; set; }

    // A collection of objects - each element is mapped by ToDto(LineItem).
    public List<LineItemDto> Lines { get; set; } = new();
}

public sealed class LineItemDto
{
    public string Product { get; set; } = string.Empty;
    public int Quantity { get; set; }
}

public enum OrderStatusDto
{
    Pending,
    Shipped,
    Delivered,
}

// ----- The mapper -----
// The class is partial and the method has no body. Synthetix fills it in.



[Mapper]
public partial class OrderMapper
{
    [MapProperty(nameof(Order.PlacedUtc), nameof(OrderDto.OrderDate))]
    [MapProperty(nameof(Order.Total), nameof(OrderDto.DisplayTotal), Use = nameof(FormatMoney))]
    [MapValue(nameof(OrderDto.Source), Value = "web")]
    [MapperIgnoreTarget(nameof(OrderDto.ExportedAt))]
    public partial OrderDto ToDto(Order order);

    // A sibling mapping. Synthetix calls it for each element of Order.Lines.
    public partial LineItemDto ToDto(LineItem line);

    // ----- v0.6 features, shown on the clean LineItem -> LineItemDto pair -----

    // Existing-instance update (design doc 7.8): fills a LineItemDto that
    // already exists instead of constructing a new one.
    public partial void Update(LineItem source, LineItemDto target);

    // Async mapper (7.11): no conversion here is actually asynchronous, so the
    // generated body runs synchronously and returns an already-completed task.
    public partial Task<LineItemDto> ToDtoAsync(LineItem line);

    // IQueryable projection (7.10): an Expression the consumer's compiler turns
    // into an expression tree, which EF Core can translate straight to SQL.
    public static partial Expression<Func<LineItem, LineItemDto>> LineProjection { get; }

    // A normal method. Synthetix picks it up as the converter named above.
    private static string FormatMoney(decimal value) => value.ToString("C");
}

// ----- The program -----

internal static class Program
{
    private static async Task Main()
    {
        var order = new Order
        {
            Id = 42,
            PlacedUtc = new DateTime(2026, 5, 22, 9, 30, 0, DateTimeKind.Utc),
            Total = 199.99m,
            Status = OrderStatus.Shipped,
            Customer = new Customer
            {
                Name = "Ada Lovelace",
                Email = "ada@example.com",
                Address = new Address { City = "London", Country = "UK" },
            },
            Lines =
            {
                new LineItem { Product = "Keyboard", Quantity = 1 },
                new LineItem { Product = "Mouse", Quantity = 2 },
            },
        };

        // The mapper is recommended to be used as an instance (design doc 4.1).
        var mapper = new OrderMapper();
        OrderDto dto = mapper.ToDto(order);

        Console.WriteLine("Mapped OrderDto:");
        Console.WriteLine($"  Id                  : {dto.Id}");
        Console.WriteLine($"  OrderDate           : {dto.OrderDate:u}");
        Console.WriteLine($"  DisplayTotal        : {dto.DisplayTotal}");
        Console.WriteLine($"  Status              : {dto.Status}");
        Console.WriteLine($"  CustomerName        : {dto.CustomerName}");
        Console.WriteLine($"  CustomerEmail       : {dto.CustomerEmail}");
        Console.WriteLine($"  CustomerAddressCity : {dto.CustomerAddressCity}");
        Console.WriteLine($"  Source              : {dto.Source}");
        Console.WriteLine($"  ExportedAt          : {(dto.ExportedAt is null ? "(not set)" : dto.ExportedAt.ToString())}");
        Console.WriteLine($"  Lines               : {dto.Lines.Count} item(s)");
        foreach (LineItemDto line in dto.Lines)
        {
            Console.WriteLine($"    - {line.Quantity} x {line.Product}");
        }

        // ----- v0.6 features, on the clean LineItem -> LineItemDto pair -----
        Console.WriteLine();
        Console.WriteLine("v0.6 features:");

        // Existing-instance update: change a DTO that already exists.
        var existing = new LineItemDto { Product = "(old)", Quantity = 0 };
        mapper.Update(new LineItem { Product = "Webcam", Quantity = 3 }, existing);
        Console.WriteLine($"  After Update         : {existing.Quantity} x {existing.Product}");

        // Async mapper: awaited like any other Task-returning method.
        LineItemDto asyncLine = await mapper.ToDtoAsync(new LineItem { Product = "Cable", Quantity = 4 });
        Console.WriteLine($"  After ToDtoAsync     : {asyncLine.Quantity} x {asyncLine.Product}");

        // IQueryable projection: compile the expression and run it.
        Func<LineItem, LineItemDto> project = OrderMapper.LineProjection.Compile();
        LineItemDto projected = project(new LineItem { Product = "Monitor", Quantity = 1 });
        Console.WriteLine($"  After LineProjection : {projected.Quantity} x {projected.Product}");
    }
}
