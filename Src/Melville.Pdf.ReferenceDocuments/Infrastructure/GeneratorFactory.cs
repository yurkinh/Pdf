namespace Melville.Pdf.ReferenceDocuments.Infrastructure;

public static class GeneratorFactory
{
    /// <summary>
    /// Every generator, found by reflection. Trimmed and AOT apps use <see cref="KnownGenerators.All"/> instead:
    /// trimming removes generator types nothing references directly.
    /// </summary>
    public static IEnumerable<IPdfGenerator> AllGenerators
    {
        [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode(
            "Finds generators by reflection; trimming removes them. Use KnownGenerators.All in trimmed apps.")]
        get => typeof(IPdfGenerator).Assembly.GetTypes()
            .Where(IsGeneratorType())
            .Select(i => (IPdfGenerator)(Activator.CreateInstance(i) ??
                                         throw new InvalidOperationException("Cannot Create: " + i)));
    }

    private static Func<Type, bool> IsGeneratorType() => i => 
        i != typeof(CreatePdfParser) && i.IsAssignableTo(typeof(IPdfGenerator)) && !i.IsAbstract;

}