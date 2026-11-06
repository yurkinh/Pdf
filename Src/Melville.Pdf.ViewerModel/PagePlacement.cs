namespace Melville.Pdf.ViewerModel;

/// <summary>
/// A page to draw: its number, where it goes in the viewport, and its rendering (null until that page has
/// rendered: the view draws a blank page meanwhile).
/// </summary>
public sealed record PagePlacement<TPage>(int Page, PageLayout Layout, RenderedPage<TPage>? Rendered);
