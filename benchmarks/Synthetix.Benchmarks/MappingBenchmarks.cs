namespace Synthetix.Benchmarks;

using System;
using System.Collections.Generic;
using AutoMapper;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;
using Synthetix;

// The benchmarks compare Synthetix against hand-written mapping code and against
// the two best-known competitors - Mapperly (also a source generator) and
// AutoMapper (reflection / expression-tree based) - across four complexity
// tiers (design doc 12.3). Synthetix is expected to match hand-written code and
// Mapperly, because all three emit direct-assignment code; AutoMapper carries
// some runtime overhead.
//
// Each tier's class has four rows: HandWritten (the baseline), Synthetix,
// Mapperly, and AutoMapper.

// ===================== Simple tier =====================
// A flat object with five primitive members.

public sealed class SimpleSource
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool Active { get; set; }
    public double Score { get; set; }
    public long Stamp { get; set; }
}

public sealed class SimpleTarget
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool Active { get; set; }
    public double Score { get; set; }
    public long Stamp { get; set; }
}

[Mapper]
public partial class SimpleMapper
{
    public partial SimpleTarget Map(SimpleSource source);
}

[MemoryDiagnoser]
public class SimpleTierBenchmarks
{
    private readonly SimpleSource _source = new()
    {
        Id = 1,
        Name = "benchmark",
        Active = true,
        Score = 9.5,
        Stamp = 1234567890,
    };

    private readonly SimpleMapper _mapper = new();

    [Benchmark(Baseline = true)]
    public SimpleTarget HandWritten() => new()
    {
        Id = _source.Id,
        Name = _source.Name,
        Active = _source.Active,
        Score = _source.Score,
        Stamp = _source.Stamp,
    };

    [Benchmark]
    public SimpleTarget Synthetix() => _mapper.Map(_source);

    [Benchmark]
    public SimpleTarget Mapperly() => Competitors.Mapperly.MapSimple(_source);

    [Benchmark]
    public SimpleTarget AutoMapper() => Competitors.AutoMapper.Map<SimpleTarget>(_source);
}

// ===================== Medium tier =====================
// More members, an enum, and one nested object reached by flattening.

public enum Priority
{
    Low,
    Normal,
    High,
}

public enum PriorityDto
{
    Low,
    Normal,
    High,
}

public sealed class MediumInner
{
    public string City { get; set; } = string.Empty;
}

public sealed class MediumSource
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public int Quantity { get; set; }
    public bool Active { get; set; }
    public DateTime Created { get; set; }
    public DateTime Updated { get; set; }
    public Priority Priority { get; set; }
    public double Score { get; set; }
    public string Tag { get; set; } = string.Empty;
    public MediumInner Inner { get; set; } = new();
}

public sealed class MediumTarget
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public int Quantity { get; set; }
    public bool Active { get; set; }
    public DateTime Created { get; set; }
    public DateTime Updated { get; set; }
    public PriorityDto Priority { get; set; }
    public double Score { get; set; }
    public string Tag { get; set; } = string.Empty;
    public string InnerCity { get; set; } = string.Empty;
}

[Mapper]
public partial class MediumMapper
{
    public partial MediumTarget Map(MediumSource source);
}

[MemoryDiagnoser]
public class MediumTierBenchmarks
{
    private readonly MediumSource _source = new()
    {
        Id = 1,
        Title = "title",
        Description = "description",
        Amount = 199.99m,
        Quantity = 3,
        Active = true,
        Created = new DateTime(2026, 1, 1),
        Updated = new DateTime(2026, 5, 1),
        Priority = Priority.High,
        Score = 4.2,
        Tag = "tag",
        Inner = new MediumInner { City = "London" },
    };

    private readonly MediumMapper _mapper = new();

    [Benchmark(Baseline = true)]
    public MediumTarget HandWritten() => new()
    {
        Id = _source.Id,
        Title = _source.Title,
        Description = _source.Description,
        Amount = _source.Amount,
        Quantity = _source.Quantity,
        Active = _source.Active,
        Created = _source.Created,
        Updated = _source.Updated,
        Priority = _source.Priority switch
        {
            Priority.Low => PriorityDto.Low,
            Priority.Normal => PriorityDto.Normal,
            Priority.High => PriorityDto.High,
            _ => throw new ArgumentOutOfRangeException(nameof(_source)),
        },
        Score = _source.Score,
        Tag = _source.Tag,
        InnerCity = _source.Inner.City,
    };

    [Benchmark]
    public MediumTarget Synthetix() => _mapper.Map(_source);

    [Benchmark]
    public MediumTarget Mapperly() => Competitors.Mapperly.MapMedium(_source);

    [Benchmark]
    public MediumTarget AutoMapper() => Competitors.AutoMapper.Map<MediumTarget>(_source);
}

// ===================== Complex tier =====================
// Several levels of nesting, flattened into a flat target.

public sealed class Level3
{
    public string Value { get; set; } = string.Empty;
}

public sealed class Level2
{
    public string Name { get; set; } = string.Empty;
    public Level3 Level3 { get; set; } = new();
}

public sealed class Level1
{
    public int Id { get; set; }
    public Level2 Level2 { get; set; } = new();
}

public sealed class ComplexSource
{
    public string Title { get; set; } = string.Empty;
    public Level1 Level1 { get; set; } = new();
}

public sealed class ComplexTarget
{
    public string Title { get; set; } = string.Empty;
    public int Level1Id { get; set; }
    public string Level1Level2Name { get; set; } = string.Empty;
    public string Level1Level2Level3Value { get; set; } = string.Empty;
}

[Mapper]
public partial class ComplexMapper
{
    public partial ComplexTarget Map(ComplexSource source);
}

[MemoryDiagnoser]
public class ComplexTierBenchmarks
{
    private readonly ComplexSource _source = new()
    {
        Title = "complex",
        Level1 = new Level1
        {
            Id = 42,
            Level2 = new Level2
            {
                Name = "level-two",
                Level3 = new Level3 { Value = "deep-value" },
            },
        },
    };

    private readonly ComplexMapper _mapper = new();

    [Benchmark(Baseline = true)]
    public ComplexTarget HandWritten() => new()
    {
        Title = _source.Title,
        Level1Id = _source.Level1.Id,
        Level1Level2Name = _source.Level1.Level2.Name,
        Level1Level2Level3Value = _source.Level1.Level2.Level3.Value,
    };

    [Benchmark]
    public ComplexTarget Synthetix() => _mapper.Map(_source);

    [Benchmark]
    public ComplexTarget Mapperly() => Competitors.Mapperly.MapComplex(_source);

    [Benchmark]
    public ComplexTarget AutoMapper() => Competitors.AutoMapper.Map<ComplexTarget>(_source);
}

// ===================== Collection tier =====================
// A list of objects, mapped element by element (design doc 7.6). This is the
// tier that stresses per-element conversion and collection construction.

public sealed class CollectionItemSource
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class CollectionItemTarget
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class CollectionSource
{
    public List<CollectionItemSource> Items { get; set; } = new();
}

public sealed class CollectionTarget
{
    public List<CollectionItemTarget> Items { get; set; } = new();
}

[Mapper]
public partial class CollectionMapper
{
    public partial CollectionTarget Map(CollectionSource source);

    // The sibling Synthetix calls for each element of Items.
    public partial CollectionItemTarget Map(CollectionItemSource source);
}

[MemoryDiagnoser]
public class CollectionTierBenchmarks
{
    // A realistic list size - big enough that per-element cost dominates.
    private const int ItemCount = 100;

    private readonly CollectionSource _source;
    private readonly CollectionMapper _mapper = new();

    public CollectionTierBenchmarks()
    {
        _source = new CollectionSource();
        for (int i = 0; i < ItemCount; i++)
        {
            _source.Items.Add(new CollectionItemSource { Id = i, Name = "item-" + i });
        }
    }

    [Benchmark(Baseline = true)]
    public CollectionTarget HandWritten()
    {
        var result = new CollectionTarget();
        foreach (CollectionItemSource item in _source.Items)
        {
            result.Items.Add(new CollectionItemTarget { Id = item.Id, Name = item.Name });
        }

        return result;
    }

    [Benchmark]
    public CollectionTarget Synthetix() => _mapper.Map(_source);

    [Benchmark]
    public CollectionTarget Mapperly() => Competitors.Mapperly.MapCollection(_source);

    [Benchmark]
    public CollectionTarget AutoMapper() => Competitors.AutoMapper.Map<CollectionTarget>(_source);
}

// ===================== Competitor mappers =====================
// Mapperly and AutoMapper, set up once and shared by every tier above.

// Mapperly is a source generator like Synthetix. Its [Mapper] attribute is
// fully qualified so it does not collide with Synthetix's own [Mapper].
[Riok.Mapperly.Abstractions.Mapper]
public partial class MapperlyMapper
{
    public partial SimpleTarget MapSimple(SimpleSource source);

    public partial MediumTarget MapMedium(MediumSource source);

    public partial ComplexTarget MapComplex(ComplexSource source);

    public partial CollectionTarget MapCollection(CollectionSource source);
}

/// <summary>Holds the single shared instance of each competitor mapper.</summary>
internal static class Competitors
{
    /// <summary>The Mapperly mapper - source-generated, like Synthetix.</summary>
    public static readonly MapperlyMapper Mapperly = new();

    /// <summary>
    /// The AutoMapper mapper - configured once, reused for every call.
    /// AutoMapper 16.x requires an ILoggerFactory; the benchmarks have no use
    /// for logging, so a NullLoggerFactory is passed.
    /// </summary>
    public static readonly IMapper AutoMapper = new MapperConfiguration(
        cfg =>
        {
            cfg.CreateMap<SimpleSource, SimpleTarget>();
            cfg.CreateMap<MediumSource, MediumTarget>();
            cfg.CreateMap<ComplexSource, ComplexTarget>();
            cfg.CreateMap<CollectionItemSource, CollectionItemTarget>();
            cfg.CreateMap<CollectionSource, CollectionTarget>();
        },
        NullLoggerFactory.Instance).CreateMapper();
}
