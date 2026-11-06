using Melville.StreamInterfaces;
﻿using Melville.Pdf.Model.Renderers.DocumentRenderers;

namespace Melville.Pdf.TextExtractor;

/// <summary>
/// This class allows text to be extracted from a PDF page or file
/// </summary>
public static class TextExtractorFacade
{
    /// <summary>
    /// Gets the text that shows up on a given page of a given Pdf file
    /// </summary>
    /// <param name="renderer">DocumentRenderer for the file to be read</param>
    /// <param name="oneBasedPageNumber">One based page number of the desired page.</param>
    /// <returns></returns>
    public static async ValueTask<string> PageTextAsync(
        this DocumentRenderer renderer, int oneBasedPageNumber) =>
        await RenderTextAsync(renderer, oneBasedPageNumber, new ConcatenateAndCollapseHyphenTarget());

    /// <summary>
    /// Gets the text that shows up on a given page of a given Pdf file.
    /// This overload leaves terminal hyphens intact.
    /// </summary>
    /// <param name="renderer">DocumentRenderer for the file to be read</param>
    /// <param name="oneBasedPageNumber">One based page number of the desired page.</param>
    /// <returns></returns>
    public static async ValueTask<string> PageTextWithTerminalHyphensAsync(
        this DocumentRenderer renderer, int oneBasedPageNumber) =>
        await RenderTextAsync(renderer, oneBasedPageNumber, new ConcatenatingTextTarget());

    /// <summary>
    /// The text of a page with the position of every glyph, in page points from the top-left corner of the page as
    /// drawn (after its default rotation). This is what a viewer searches, selects and highlights.
    /// </summary>
    /// <param name="renderer">The document.</param>
    /// <param name="oneBasedPageNumber">The page; the first page is 1.</param>
    public static async ValueTask<PageText> PageTextLayerAsync(
        this DocumentRenderer renderer, int oneBasedPageNumber)
    {
        var builder = new TextLayerBuilder();
        await renderer.RenderPageToAsync(oneBasedPageNumber, (rect, rotation) =>
        {
            var target = new TextLayerRender(builder);
            // One unit per point, from the top-left corner, as the page is drawn.
            renderer.InitializeRenderTarget(target, rect, Math.Abs(rect.Width), Math.Abs(rect.Height), rotation);
            return target;
        }).CA();
        return builder.Build();
    }

    private static async Task<string> RenderTextAsync(
        DocumentRenderer renderer, int oneBasedPageNumber, ConcatenatingTextTarget target)
    {
        await renderer.RenderPageToAsync(oneBasedPageNumber,
            (_,__)=>new ExtractTextRender(target));
        return target.AllText();
    }
}