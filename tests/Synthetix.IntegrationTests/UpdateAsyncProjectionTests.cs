namespace Synthetix.IntegrationTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Synthetix;
using Xunit;

// Behavioural tests for the v0.6 features: existing-instance update (design doc
// 7.8), async mappers (7.11), and IQueryable projection (7.10). Each test runs a
// mapper the generator actually built.

public class UpdateAsyncProjectionTests
{
    // ===================== existing-instance update (7.8) =====================

    [Fact]
    public void An_update_fills_an_object_that_already_exists()
    {
        var source = new Profile { Name = "new name", Age = 41 };
        var target = new ProfileDto { Name = "old name", Age = 1 };

        new ProfileMapper().Apply(source, target);

        Assert.Equal("new name", target.Name);
        Assert.Equal(41, target.Age);
    }

    // ===================== async mappers (7.11) =====================

    [Fact]
    public async Task An_async_mapper_with_no_async_work_still_returns_a_task()
    {
        PingDto dto = await new PingMapper().ToDtoAsync(new Ping { N = 7 });

        Assert.Equal(7, dto.N);
    }

    [Fact]
    public async Task An_async_mapper_awaits_an_async_user_mapping()
    {
        TicketDto dto = await new TicketMapper().ToDtoAsync(new Ticket { Code = 5 });

        // The async user mapping turned the int code into a string.
        Assert.Equal("T-5", dto.Code);
    }

    // ===================== IQueryable projection (7.10) =====================

    [Fact]
    public void A_projection_maps_a_flat_object()
    {
        Func<Book, BookDto> project = BookMapper.Projection.Compile();

        BookDto dto = project(new Book { Id = 3, Title = "Roslyn" });

        Assert.Equal(3, dto.Id);
        Assert.Equal("Roslyn", dto.Title);
    }

    [Fact]
    public void A_projection_runs_inside_a_LINQ_query()
    {
        var books = new[] { new Book { Id = 1, Title = "A" } }.AsQueryable();

        BookDto dto = books.Select(BookMapper.Projection).Single();

        Assert.Equal(1, dto.Id);
        Assert.Equal("A", dto.Title);
    }

    [Fact]
    public void A_projection_flattens_a_nested_path()
    {
        Func<Company, CompanyDto> project = CompanyMapper.Projection.Compile();

        CompanyDto dto = project(new Company
        {
            Name = "Acme",
            Owner = new Founder { FullName = "Ada Lovelace" },
        });

        Assert.Equal("Acme", dto.Name);
        Assert.Equal("Ada Lovelace", dto.OwnerFullName);
    }

    [Fact]
    public void A_projection_projects_a_collection_member()
    {
        Func<Team, TeamDto> project = TeamMapper.Projection.Compile();

        TeamDto dto = project(new Team
        {
            Name = "Reds",
            Players = { new Player { Name = "Sam" }, new Player { Name = "Lee" } },
        });

        Assert.Equal("Reds", dto.Name);
        Assert.Equal(2, dto.Players.Count);
        Assert.Equal("Sam", dto.Players[0].Name);
        Assert.Equal("Lee", dto.Players[1].Name);
    }
}

// ===================== update types and mapper =====================

public sealed class Profile
{
    public string Name { get; set; } = string.Empty;
    public int Age { get; set; }
}

public sealed class ProfileDto
{
    public string Name { get; set; } = string.Empty;
    public int Age { get; set; }
}

[Mapper]
public partial class ProfileMapper
{
    public partial void Apply(Profile source, ProfileDto target);
}

// ===================== async types and mappers =====================

public sealed class Ping
{
    public int N { get; set; }
}

public sealed class PingDto
{
    public int N { get; set; }
}

[Mapper]
public partial class PingMapper
{
    // No async conversion is needed, so the generated body is synchronous and
    // returns an already-completed ValueTask.
    public partial ValueTask<PingDto> ToDtoAsync(Ping ping);
}

public sealed class Ticket
{
    public int Code { get; set; }
}

public sealed class TicketDto
{
    public string Code { get; set; } = string.Empty;
}

[Mapper]
public partial class TicketMapper
{
    public partial Task<TicketDto> ToDtoAsync(Ticket ticket);

    // An async user mapping: int -> Task<string>. The generated method awaits it.
    private static async Task<string> Render(int code)
    {
        await Task.Yield();
        return "T-" + code;
    }
}

// ===================== projection types and mappers =====================

public sealed class Book
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
}

public sealed class BookDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
}

[Mapper]
public partial class BookMapper
{
    public static partial Expression<Func<Book, BookDto>> Projection { get; }
}

public sealed class Founder
{
    public string FullName { get; set; } = string.Empty;
}

public sealed class Company
{
    public string Name { get; set; } = string.Empty;
    public Founder Owner { get; set; } = new();
}

public sealed class CompanyDto
{
    public string Name { get; set; } = string.Empty;
    public string OwnerFullName { get; set; } = string.Empty;
}

[Mapper]
public partial class CompanyMapper
{
    public static partial Expression<Func<Company, CompanyDto>> Projection { get; }
}

public sealed class Player
{
    public string Name { get; set; } = string.Empty;
}

public sealed class PlayerDto
{
    public string Name { get; set; } = string.Empty;
}

public sealed class Team
{
    public string Name { get; set; } = string.Empty;
    public List<Player> Players { get; set; } = new();
}

public sealed class TeamDto
{
    public string Name { get; set; } = string.Empty;
    public List<PlayerDto> Players { get; set; } = new();
}

[Mapper]
public partial class TeamMapper
{
    public static partial Expression<Func<Team, TeamDto>> Projection { get; }
}
