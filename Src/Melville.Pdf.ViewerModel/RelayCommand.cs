using System.ComponentModel;
using System.Windows.Input;

namespace Melville.Pdf.ViewerModel;

/// <summary>
/// A command whose CanExecute is re-queried whenever the owning view model raises PropertyChanged.
/// </summary>
internal sealed class RelayCommand : ICommand
{
    private readonly Action execute;
    private readonly Func<bool> canExecute;

    public RelayCommand(Action execute, Func<bool> canExecute, INotifyPropertyChanged owner)
    {
        this.execute = execute;
        this.canExecute = canExecute;
        owner.PropertyChanged += (_, _) => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool CanExecute(object? parameter) => canExecute();

    public void Execute(object? parameter)
    {
        if (canExecute()) execute();
    }

    public event EventHandler? CanExecuteChanged;
}
