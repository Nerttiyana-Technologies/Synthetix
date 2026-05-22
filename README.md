# Synthetix

**A compile-time, source-generated object-to-object mapper for .NET — as fast as
hand-written code, with mappings that are auditable and drift-checked.**

Synthetix turns the boring, error-prone job of copying values from one object to
another into plain C# that the compiler writes for you. You declare a `partial`
mapper class; a Roslyn source generator reads your types at build time and emits
ordinary, readable mapping code into your project.

Because the mapping is generated while your project compiles, there is **no
runtime reflection, no expression-tree compilation, and no startup warm-up**. The
generated code is real C# you can set a breakpoint inside, and it works under
Native AOT and assembly trimming with nothing extra to configure.

Synthetix is not here to replace the good tools that already exist —
[Mapperly](https://github.com/riok/mapperly) and
[AutoMapper](https://github.com/AutoMapper/AutoMapper) are mature and well
designed. Synthetix focuses its effort on one thing those tools leave largely
untouched: making the mapping itself something you can **see, review, and
trust** — see the mapping manifest below.

## Install

```xml
<PackageReference Include="Synthetix" Version="0.1.0"
                  PrivateAssets="all" ExcludeAssets="runtime" />
```

The package contains only a source generator. Your built assembly carries **no
Synthetix dependency at runtime** — nothing to load, trim, or version-conflict.

## Quick start

Declare a mapper. The methods are `partial` and have no body — Synthetix fills
them in:

```csharp
using Synthetix;

[Mapper]
public partial class OrderMapper
{
    public partial OrderDto ToDto(Order order);
}
```

That is all. Call `new OrderMapper().ToDto(order)` and you get a fully mapped
`OrderDto`. If a property cannot be mapped, your **build fails** with a precise
error instead of shipping a silent `null`.

## What Synthetix does

- **Same-name mapping** — properties with matching names and compatible types
  are mapped automatically.
- **Flattening / unflattening** — `Order.Customer.Name` maps to
  `OrderCustomerName` and back, by naming convention.
- **Compile-time diagnostics** — broken, incomplete, or ambiguous mappings
  become compiler errors and warnings (`SYNTX001`–`SYNTX017`) with exact source
  locations.
- **Custom member configuration** — attributes to rename, ignore, supply
  constant values, or route a member through your own conversion method.
- **The mapping manifest** — the capability Synthetix invests in most.

## The mapping manifest

For every mapper, Synthetix writes a **mapping manifest** — a readable record of
every mapping decision (which source fed which target, which rule resolved it,
what was ignored, and a coverage count). It is meant to be committed to source
control, so a mapping becomes something a reviewer can read in a pull request.

On every build, Synthetix compares your committed manifest against the current
mapping. If they differ — a newly unmapped field, a changed rule — it reports
`SYNTX017`. Teams keep that a warning locally and an error in CI, so a change in
mapping behaviour can never merge by accident. Refresh the manifest explicitly
with:

```
dotnet build -t:SynthetixUpdateManifest
```

The manifest is on by default. Turn it off with
`<SynthetixManifest>false</SynthetixManifest>`.

## Build from source

This repository contains the full solution (see `docs/Synthetix-Design.md` for
the design):

```
dotnet build Synthetix.sln
dotnet test  Synthetix.sln
dotnet run --project samples/Synthetix.Sample
```

Requires the .NET 10 SDK. The generator project itself targets `netstandard2.0`
because it is loaded into the compiler — that is a hard requirement, separate
from the .NET 10 your own code targets.

## License

MIT — see [LICENSE](LICENSE).
