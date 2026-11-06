using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Melville.Pdf.LowLevel.Model.Conventions;
using Melville.Pdf.LowLevel.Model.Objects;
using Melville.Pdf.LowLevel.Model.Objects.StringEncodings;
using Melville.Pdf.LowLevel.Model.Wrappers;
using Melville.Pdf.Model.Documents;
using Melville.StreamInterfaces;

namespace Melville.Pdf.Model.Renderers.DocumentRenderers;

/// <summary>
/// Reads the link annotations of a page (ISO 32000-2 12.5.6.5) and resolves where they go: explicit and named
/// destinations (12.3.2), and GoTo, URI and Named actions (12.6.4). Links of other kinds, and links that cannot
/// be resolved, are skipped: a damaged link never stops the others from working.
/// </summary>
internal readonly struct LinkReader
{
    private const int MaxIndirection = 8; // a destination can name a dictionary that names another destination

    private readonly DocumentRenderer document;

    public LinkReader(DocumentRenderer document) => this.document = document;

    public async ValueTask<IReadOnlyList<PdfLink>> ReadAsync(int oneBasedPageNumber)
    {
        var page = await document.PageAsync(oneBasedPageNumber).CA();
        if (await page.LowLevel.GetOrNullAsync<PdfArray>(KnownNames.Annots).CA() is not { } annots) return [];
        var (rotation, _) = await document.VisibleBoxTransformAsync(page).CA();
        var links = new List<PdfLink>();
        for (int i = 0; i < annots.Count; i++)
        {
            try
            {
                if ((await annots[i].CA()).TryGet(out PdfDictionary? annot) &&
                    await LinkAsync(annot, oneBasedPageNumber, rotation).CA() is { } link)
                    links.Add(link);
            }
            catch (Exception)
            {
                // A malformed annotation is not a link; keep reading the others.
            }
        }
        return links;
    }

    private async ValueTask<PdfLink?> LinkAsync(PdfDictionary annot, int pageNumber, Matrix3x2 rotation)
    {
        if (!(await annot.GetOrNullAsync(KnownNames.Subtype).CA()).Equals(KnownNames.Link)) return null;
        if (await annot.GetOrNullAsync<PdfArray>(KnownNames.Rect).CA() is not { Count: 4 } rectArray) return null;
        var target = await annot.GetOrNullAsync<PdfDictionary>(KnownNames.A).CA() is { } action
            ? await ActionAsync(action, pageNumber).CA()
            : await DestinationAsync(await annot.GetOrNullAsync(KnownNames.Dest).CA(), 0).CA();
        return target is null ? null : new PdfLink((await PdfRect.CreateAsync(rectArray).CA()).Transform(rotation), target);
    }

    private async ValueTask<PdfLinkTarget?> ActionAsync(PdfDictionary action, int pageNumber)
    {
        var kind = await action.GetOrNullAsync(KnownNames.S).CA();
        if (kind.Equals(KnownNames.URI))
            return await action.GetOrNullAsync(KnownNames.URI).CA() is { IsString: true } uri
                ? new PdfUriLinkTarget(uri.DecodedString())
                : null;
        if (kind.Equals(KnownNames.GoTo))
            return await DestinationAsync(await action.GetOrNullAsync(KnownNames.D).CA(), 0).CA();
        if (kind.Equals(KnownNames.Named))
            return NamedAction(await action.GetOrNullAsync(KnownNames.N).CA(), pageNumber);
        return null;
    }

    private PdfLinkTarget? NamedAction(PdfDirectObject name, int pageNumber)
    {
        var page = name switch
        {
            _ when name.Equals(KnownNames.NextPage) => pageNumber + 1,
            _ when name.Equals(KnownNames.PrevPage) => pageNumber - 1,
            _ when name.Equals(KnownNames.FirstPage) => 1,
            _ when name.Equals(KnownNames.LastPage) => document.TotalPages,
            _ => 0,
        };
        return page >= 1 && page <= document.TotalPages ? new PdfPageLinkTarget(page) : null;
    }

    private async ValueTask<PdfLinkTarget?> DestinationAsync(PdfDirectObject destination, int depth)
    {
        if (depth > MaxIndirection) return null;
        if (destination.TryGet(out PdfArray? explicitDestination)) return await ExplicitAsync(explicitDestination).CA();
        if (destination.TryGet(out PdfDictionary? holder)) // a named destination's value can be << /D [...] >>
            return await DestinationAsync(await holder.GetOrNullAsync(KnownNames.D).CA(), depth + 1).CA();
        if (destination.IsName || destination.IsString)
            return await DestinationAsync(await document.NamedDestinationAsync(destination).CA(), depth + 1).CA();
        return null;
    }

    // [page /XYZ left top zoom], [page /FitH top], [page /FitR left bottom right top], ...
    private async ValueTask<PdfLinkTarget?> ExplicitAsync(PdfArray destination)
    {
        if (destination.Count < 1) return null;
        var pageObject = await destination[0].CA();
        var pageNumber = pageObject.TryGet(out PdfDictionary? pageDictionary)
            ? await document.PageNumberOfAsync(pageDictionary).CA()
            : pageObject.TryGet(out long index) ? (int)index + 1 // a page index: meant for remote links, but seen locally
            : 0;
        if (pageNumber < 1 || pageNumber > document.TotalPages) return null;

        var kind = destination.Count > 1 ? await destination[1].CA() : default;
        var (left, top) = kind switch
        {
            _ when kind.Equals(KnownNames.XYZ) => (await NumberAsync(destination, 2).CA(), await NumberAsync(destination, 3).CA()),
            _ when kind.Equals(KnownNames.FitH) || kind.Equals(KnownNames.FitBH) => (null, await NumberAsync(destination, 2).CA()),
            _ when kind.Equals(KnownNames.FitV) || kind.Equals(KnownNames.FitBV) => (await NumberAsync(destination, 2).CA(), null),
            _ when kind.Equals(KnownNames.FitR) => (await NumberAsync(destination, 2).CA(), await NumberAsync(destination, 5).CA()),
            _ => ((double?)null, (double?)null), // /Fit, /FitB: the whole page
        };
        if (left is null && top is null) return new PdfPageLinkTarget(pageNumber);
        return await InVisibleBoxAsync(pageNumber, left, top).CA();
    }

    // Destinations are in the target page's default user space; links report the rotated (visible) page box.
    private async ValueTask<PdfLinkTarget> InVisibleBoxAsync(int pageNumber, double? left, double? top)
    {
        var page = await document.PageAsync(pageNumber).CA();
        var (rotation, crop) = await document.VisibleBoxTransformAsync(page).CA();
        var point = Vector2.Transform(new Vector2((float)(left ?? crop.Left), (float)(top ?? crop.Top)), rotation);
        // A quarter turn swaps the axes, so a missing coordinate moves to the other one.
        var quarterTurn = Math.Abs(await page.GetDefaultRotationAsync().CA() / 90 % 2) == 1;
        var (keepX, keepY) = quarterTurn ? (top is null, left is null) : (left is null, top is null);
        return new PdfPageLinkTarget(pageNumber, keepX ? null : point.X, keepY ? null : point.Y);
    }

    private static async ValueTask<double?> NumberAsync(PdfArray array, int index) =>
        index < array.Count && (await array[index].CA()) is { IsNumber: true } number ? number.Get<double>() : null;
}
