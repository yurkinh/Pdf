using System.ComponentModel;
using Melville.Pdf.Model.OptionalContent;

namespace Melville.Pdf.ViewerModel;

/// <summary>
/// One row of the optional content (layers) tree, flattened for a list control: Depth gives the indent.
/// Setting Visible changes the document's optional content state, which re-renders the page.
/// </summary>
public sealed class LayerRow : INotifyPropertyChanged
{
    private readonly IOptionalContentDisplayGroup group;

    internal LayerRow(IOptionalContentDisplayGroup group, int depth)
    {
        this.group = group;
        Depth = depth;
    }

    /// <summary>The layer name shown to the user.</summary>
    public string Name => group.Name;

    /// <summary>Nesting level, 0 for top-level rows.</summary>
    public int Depth { get; }

    /// <summary>False for heading rows that group other layers and cannot be toggled.</summary>
    public bool ShowCheck => group.ShowCheck;

    /// <summary>Whether the layer is drawn.</summary>
    public bool Visible
    {
        get => group.Visible;
        set
        {
            if (group.Visible == value) return;
            group.Visible = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Visible)));
        }
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Flatten a display tree in display order: each group followed by its children.</summary>
    public static IReadOnlyList<LayerRow> Flatten(IReadOnlyList<IOptionalContentDisplayGroup> groups)
    {
        var rows = new List<LayerRow>();
        void Add(IReadOnlyList<IOptionalContentDisplayGroup> level, int depth)
        {
            foreach (var group in level)
            {
                rows.Add(new LayerRow(group, depth));
                Add(group.Children, depth + 1);
            }
        }
        Add(groups, 0);
        return rows;
    }
}
