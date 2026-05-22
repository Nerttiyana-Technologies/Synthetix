namespace Synthetix.Generator.Tests;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

/// <summary>
/// A small helper that runs the Synthetix generator over a piece of C# source
/// and hands back what it produced. Every generator test uses this.
/// </summary>
internal static class GeneratorTestHarness
{
    // Every assembly the test runtime has loaded - used as the metadata
    // references for the in-memory compilation.
    private static readonly MetadataReference[] RuntimeReferences =
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(System.IO.Path.PathSeparator)
        .Where(path => path.Length > 0)
        .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
        .ToArray();

    /// <summary>Runs the generator once over the given source.</summary>
    public static HarnessResult Run(
        string source,
        IEnumerable<(string Path, string Text)>? additionalFiles = null)
    {
        Compilation compilation = CreateCompilation(source);

        var additionalTexts = (additionalFiles ?? Enumerable.Empty<(string, string)>())
            .Select(file => (AdditionalText)new InMemoryAdditionalText(file.Path, file.Text))
            .ToImmutableArray();

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { new SynthetixGenerator().AsSourceGenerator() },
            additionalTexts: additionalTexts,
            parseOptions: (CSharpParseOptions)CSharpSyntaxTree.ParseText(source).Options,
            optionsProvider: null,
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true));

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation, out Compilation outputCompilation, out _);

        GeneratorDriverRunResult runResult = driver.GetRunResult();

        var generated = runResult.GeneratedTrees
            .ToDictionary(
                tree => System.IO.Path.GetFileName(tree.FilePath),
                tree => tree.ToString());

        return new HarnessResult
        {
            Driver = driver,
            OutputCompilation = outputCompilation,
            Diagnostics = runResult.Diagnostics,
            GeneratedSources = generated,
        };
    }

    /// <summary>Builds a compilation from one source file plus the whole runtime.</summary>
    public static Compilation CreateCompilation(string source)
        => CSharpCompilation.Create(
            assemblyName: "SynthetixTests",
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(source) },
            references: RuntimeReferences,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

    /// <summary>An additional file that lives only in memory, for manifest tests.</summary>
    private sealed class InMemoryAdditionalText : AdditionalText
    {
        private readonly string _text;

        public InMemoryAdditionalText(string path, string text)
        {
            Path = path;
            _text = text;
        }

        public override string Path { get; }

        public override SourceText GetText(CancellationToken cancellationToken = default)
            => SourceText.From(_text, Encoding.UTF8);
    }
}

/// <summary>Everything one generator run produced.</summary>
internal sealed class HarnessResult
{
    public required GeneratorDriver Driver { get; init; }

    public required Compilation OutputCompilation { get; init; }

    /// <summary>Diagnostics the generator itself reported (the SYNTX#### ones).</summary>
    public required ImmutableArray<Diagnostic> Diagnostics { get; init; }

    /// <summary>Generated files, keyed by their short file name.</summary>
    public required IReadOnlyDictionary<string, string> GeneratedSources { get; init; }

    /// <summary>All generated source text joined together - handy for "contains" checks.</summary>
    public string AllGeneratedText => string.Join("\n", GeneratedSources.Values);

    /// <summary>Finds the generated file whose name contains the given fragment.</summary>
    public string GeneratedFile(string nameFragment)
        => GeneratedSources
            .First(pair => pair.Key.Contains(nameFragment, StringComparison.Ordinal))
            .Value;

    /// <summary>The ids of every diagnostic reported, for easy assertions.</summary>
    public IReadOnlyList<string> DiagnosticIds
        => Diagnostics.Select(d => d.Id).OrderBy(id => id).ToList();
}
