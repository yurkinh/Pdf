using Melville.Pdf.ReferenceDocuments.Infrastructure;
using Xunit;

namespace Melville.Pdf.MauiGraphics.Tests;

/// <summary>
/// KnownGenerators (the explicit list trimmed apps use) must name exactly the generators reflection finds.
/// After adding a reference document, rerun: ./.dotnet/dotnet run tools/ListReferenceGenerators.cs
/// </summary>
public class KnownGeneratorsTest
{
    [Fact]
    public void ListsEveryGenerator()
    {
        var found = GeneratorFactory.AllGenerators.Select(g => g.Prefix).Order(StringComparer.Ordinal).ToList();
        var listed = KnownGenerators.All.Select(g => g.Prefix).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(found, listed);
    }
}
