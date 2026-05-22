namespace Synthetix.IntegrationTests;

using System.Collections.Generic;
using System.Collections.Immutable;
using Synthetix;
using Xunit;

// Behavioural tests for the v0.5 features: collection-element mapping (7.6) and
// polymorphic dispatch (7.7). These run mappers the generator actually built.

public class CollectionAndPolymorphismTests
{
    [Fact]
    public void A_list_of_objects_is_deep_mapped()
    {
        var cart = new Cart
        {
            Items = { new Item { Name = "apple" }, new Item { Name = "pear" } },
        };

        CartDto dto = new CartMapper().ToDto(cart);

        Assert.Equal(2, dto.Items.Count);
        Assert.Equal("apple", dto.Items[0].Name);
        Assert.Equal("pear", dto.Items[1].Name);
    }

    [Fact]
    public void An_array_is_mapped_with_element_widening()
    {
        var cart = new Cart { Codes = new[] { 1, 2, 3 } };

        CartDto dto = new CartMapper().ToDto(cart);

        Assert.Equal(new long[] { 1L, 2L, 3L }, dto.Codes);
    }

    [Fact]
    public void A_dictionary_is_deep_mapped()
    {
        var catalog = new Catalog { Prices = { ["pen"] = 2, ["pad"] = 5 } };

        CatalogDto dto = new CatalogMapper().ToDto(catalog);

        Assert.Equal(2L, dto.Prices["pen"]);
        Assert.Equal(5L, dto.Prices["pad"]);
    }

    [Fact]
    public void An_immutable_list_is_deep_mapped()
    {
        var source = new Numbers { Items = ImmutableList.Create(10, 20) };

        NumbersDto dto = new NumbersMapper().ToDto(source);

        Assert.Equal(new long[] { 10L, 20L }, dto.Items);
    }

    [Fact]
    public void A_polymorphic_mapping_dispatches_on_the_runtime_type()
    {
        Shape circle = new Circle { Name = "c", Radius = 2.5 };
        Shape square = new Square { Name = "s", Side = 4.0 };
        var mapper = new ShapeMapper();

        ShapeDto circleDto = mapper.ToDto(circle);
        ShapeDto squareDto = mapper.ToDto(square);

        CircleDto mappedCircle = Assert.IsType<CircleDto>(circleDto);
        Assert.Equal("c", mappedCircle.Name);
        Assert.Equal(2.5, mappedCircle.Radius);

        SquareDto mappedSquare = Assert.IsType<SquareDto>(squareDto);
        Assert.Equal(4.0, mappedSquare.Side);
    }
}

// ===================== collection types and mappers =====================

public sealed class Item
{
    public string Name { get; set; } = string.Empty;
}

public sealed class ItemDto
{
    public string Name { get; set; } = string.Empty;
}

public sealed class Cart
{
    public List<Item> Items { get; set; } = new();
    public int[] Codes { get; set; } = System.Array.Empty<int>();
}

public sealed class CartDto
{
    public List<ItemDto> Items { get; set; } = new();
    public long[] Codes { get; set; } = System.Array.Empty<long>();
}

[Mapper]
public partial class CartMapper
{
    public partial CartDto ToDto(Cart cart);

    // A sibling mapping the generator uses for each Items element.
    public partial ItemDto ToDto(Item item);
}

public sealed class Catalog
{
    public Dictionary<string, int> Prices { get; set; } = new();
}

public sealed class CatalogDto
{
    public Dictionary<string, long> Prices { get; set; } = new();
}

[Mapper]
public partial class CatalogMapper
{
    public partial CatalogDto ToDto(Catalog catalog);
}

public sealed class Numbers
{
    public ImmutableList<int> Items { get; set; } = ImmutableList<int>.Empty;
}

public sealed class NumbersDto
{
    public ImmutableList<long> Items { get; set; } = ImmutableList<long>.Empty;
}

[Mapper]
public partial class NumbersMapper
{
    public partial NumbersDto ToDto(Numbers numbers);
}

// ===================== polymorphic types and mapper =====================

public abstract class Shape
{
    public string Name { get; set; } = string.Empty;
}

public sealed class Circle : Shape
{
    public double Radius { get; set; }
}

public sealed class Square : Shape
{
    public double Side { get; set; }
}

public abstract class ShapeDto
{
    public string Name { get; set; } = string.Empty;
}

public sealed class CircleDto : ShapeDto
{
    public double Radius { get; set; }
}

public sealed class SquareDto : ShapeDto
{
    public double Side { get; set; }
}

[Mapper]
public partial class ShapeMapper
{
    [MapDerivedType(typeof(Circle), typeof(CircleDto))]
    [MapDerivedType(typeof(Square), typeof(SquareDto))]
    public partial ShapeDto ToDto(Shape shape);

    public partial CircleDto ToDto(Circle circle);

    public partial SquareDto ToDto(Square square);
}
