// Entry point for the Synthetix performance suite.
//
// Run all benchmarks in Release configuration:
//   dotnet run -c Release --project benchmarks/Synthetix.Benchmarks -- --filter *

using BenchmarkDotNet.Running;

namespace Synthetix.Benchmarks;

internal static class Program
{
    private static void Main(string[] args)
        => BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
