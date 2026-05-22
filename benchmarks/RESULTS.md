# Benchmark results

This file holds the measured numbers from the Synthetix performance suite
(`benchmarks/Synthetix.Benchmarks`). It is refreshed on every release.

To produce the numbers locally:

```
dotnet run -c Release --project benchmarks/Synthetix.Benchmarks -- --filter *
```

The suite measures four complexity tiers - Simple, Medium, Complex, and
Collection (design doc section 12.3). Each tier compares four approaches as
rows in one table:

- **HandWritten** - the baseline; mapping code written by hand.
- **Synthetix** - this library; compile-time source generation.
- **Mapperly** - a competing compile-time source generator.
- **AutoMapper** - the established reflection / expression-tree based mapper.

The expectation, from each approach's design, is that Synthetix lands on the
same time-per-call and allocation numbers as hand-written code and as Mapperly,
because all three emit direct-assignment code; AutoMapper is expected to carry
measurable runtime overhead.

|-------------|-----------|-----------|-----------|-------|---------|--------|-----------|-------------|
| Method      | Mean      | Error     | StdDev    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------:|----------:|----------:|----------:|------:|--------:|-------:|----------:|------------:|
| HandWritten |  4.209 ns | 0.0086 ns | 0.0081 ns |  1.00 |    0.00 | 0.0057 |      48 B |        1.00 |
| Synthetix   |  4.443 ns | 0.0076 ns | 0.0068 ns |  1.06 |    0.00 | 0.0057 |      48 B |        1.00 |
| Mapperly    |  4.193 ns | 0.0140 ns | 0.0124 ns |  1.00 |    0.00 | 0.0057 |      48 B |        1.00 |
| AutoMapper  | 37.466 ns | 0.0871 ns | 0.0772 ns |  8.90 |    0.02 | 0.0057 |      48 B |        1.00 |
|-------------|-----------|-----------|-----------|-------|---------|--------|-----------|-------------|

