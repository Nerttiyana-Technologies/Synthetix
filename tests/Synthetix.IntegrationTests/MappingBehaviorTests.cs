namespace Synthetix.IntegrationTests;

using System.Globalization;
using Synthetix;
using Xunit;

// These tests exercise mappers that the generator actually built while this
// project compiled. If the generated code were wrong, these would fail.

public class MappingBehaviorTests
{
    [Fact]
    public void Same_name_members_are_copied()
    {
        PersonDto dto = new PersonMapper().ToDto(new Person { Id = 7, Name = "Ada" });

        Assert.Equal(7, dto.Id);
        Assert.Equal("Ada", dto.Name);
    }

    [Fact]
    public void Implicit_numeric_widening_is_applied()
    {
        CountsDto dto = new CountsMapper().ToDto(new Counts { Value = 5 });

        Assert.Equal(5L, dto.Value);
    }

    [Fact]
    public void A_non_nullable_value_maps_into_a_nullable_one()
    {
        OptionalDto dto = new OptionalMapper().ToDto(new Required { Value = 9 });

        Assert.Equal(9, dto.Value);
    }

    [Fact]
    public void A_nested_value_is_flattened()
    {
        GeoPersonDto dto = new GeoMapper().ToDto(
            new GeoPerson { Address = new GeoAddress { City = "London" } });

        Assert.Equal("London", dto.AddressCity);
    }

    [Fact]
    public void An_enum_is_mapped_by_member_name()
    {
        PaintDto dto = new PaintMapper().ToDto(new Paint { Hue = Hue.Green });

        Assert.Equal(HueDto.Green, dto.Hue);
    }

    [Fact]
    public void Customization_attributes_are_honoured()
    {
        InvoiceDto dto = new InvoiceMapper().ToDto(new Invoice { Amount = 12.5m });

        Assert.Equal("12.5", dto.Amount);   // routed through the Format converter
        Assert.Equal("api", dto.Channel);   // a [MapValue] constant
        Assert.Equal(0, dto.Internal);      // left at default by [MapperIgnoreTarget]
    }

    [Fact]
    public void A_record_target_is_built_through_its_constructor()
    {
        PointDto dto = new PointMapper().ToDto(new PointSource { X = 3, Y = 4 });

        Assert.Equal(3, dto.X);
        Assert.Equal(4, dto.Y);
    }

    [Fact]
    public void A_static_mapper_works_the_same_as_an_instance_one()
    {
        PersonDto dto = StaticPersonMapper.ToDto(new Person { Id = 1, Name = "Grace" });

        Assert.Equal(1, dto.Id);
        Assert.Equal("Grace", dto.Name);
    }
}

// ===================== types and mappers used by the tests =====================

public sealed class Person
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class PersonDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

[Mapper]
public partial class PersonMapper
{
    public partial PersonDto ToDto(Person p);
}

[Mapper]
public static partial class StaticPersonMapper
{
    public static partial PersonDto ToDto(Person p);
}

public sealed class Counts
{
    public int Value { get; set; }
}

public sealed class CountsDto
{
    public long Value { get; set; }
}

[Mapper]
public partial class CountsMapper
{
    public partial CountsDto ToDto(Counts c);
}

public sealed class Required
{
    public int Value { get; set; }
}

public sealed class OptionalDto
{
    public int? Value { get; set; }
}

[Mapper]
public partial class OptionalMapper
{
    public partial OptionalDto ToDto(Required r);
}

public sealed class GeoAddress
{
    public string City { get; set; } = string.Empty;
}

public sealed class GeoPerson
{
    public GeoAddress Address { get; set; } = new();
}

public sealed class GeoPersonDto
{
    public string AddressCity { get; set; } = string.Empty;
}

[Mapper]
public partial class GeoMapper
{
    public partial GeoPersonDto ToDto(GeoPerson p);
}

public enum Hue
{
    Red,
    Green,
    Blue,
}

public enum HueDto
{
    Red,
    Green,
    Blue,
}

public sealed class Paint
{
    public Hue Hue { get; set; }
}

public sealed class PaintDto
{
    public HueDto Hue { get; set; }
}

[Mapper]
public partial class PaintMapper
{
    public partial PaintDto ToDto(Paint p);
}

public sealed class Invoice
{
    public decimal Amount { get; set; }
}

public sealed class InvoiceDto
{
    public string Amount { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public int Internal { get; set; }
}

[Mapper]
public partial class InvoiceMapper
{
    [MapProperty(nameof(Invoice.Amount), nameof(InvoiceDto.Amount), Use = nameof(Format))]
    [MapValue(nameof(InvoiceDto.Channel), Value = "api")]
    [MapperIgnoreTarget(nameof(InvoiceDto.Internal))]
    public partial InvoiceDto ToDto(Invoice i);

    private static string Format(decimal value) => value.ToString(CultureInfo.InvariantCulture);
}

public sealed class PointSource
{
    public int X { get; set; }
    public int Y { get; set; }
}

public sealed record PointDto(int X, int Y);

[Mapper]
public partial class PointMapper
{
    public partial PointDto ToDto(PointSource p);
}
