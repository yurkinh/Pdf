using Melville.Pdf.LowLevel.Parsing.ParserContext;

namespace Plugin.Maui.Pdf.TestApp.Viewer;

/// <summary>
/// Supplies passwords for an encrypted document: first the known one, if any, then whatever the user types into
/// a masked prompt. Cancelling the prompt cancels the open. The reader calls this from a worker thread.
/// </summary>
public sealed class PasswordPrompt(string documentTitle, string? knownPassword) : IPasswordSource
{
    private int attempts;

    public async ValueTask<(string?, PasswordType)> GetPasswordAsync()
    {
        attempts++;
        if (attempts == 1 && knownPassword is not null) return (knownPassword, PasswordType.User);
        var retry = attempts > (knownPassword is null ? 1 : 2);
        var message = retry
            ? "That password did not open the document. Try again."
            : $"“{documentTitle}” is protected. Enter its password.";
        var password = await MainThread.InvokeOnMainThreadAsync(() => PasswordPage.AskAsync(message));
        return (password, PasswordType.User);
    }
}

/// <summary>A modal page with a masked entry. DisplayPromptAsync cannot hide what is typed.</summary>
internal sealed class PasswordPage : ContentPage
{
    // Resume the reader on the thread pool, not inside the button handler on the UI thread.
    private readonly TaskCompletionSource<string?> result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Entry entry;
    private bool closing;

    private PasswordPage(string message)
    {
        Title = "Password";
        entry = new Entry
        {
            IsPassword = true, Placeholder = "Password", AutomationId = "Password.Entry",
            ReturnType = ReturnType.Go, IsSpellCheckEnabled = false, IsTextPredictionEnabled = false,
        };
        SemanticProperties.SetDescription(entry, "Document password");
        entry.Completed += (_, _) => _ = CloseAsync(entry.Text ?? "");

        var open = new Button { Text = "Open", AutomationId = "Password.Open" };
        open.Clicked += (_, _) => _ = CloseAsync(entry.Text ?? "");
        var cancel = new Button { Text = "Cancel", AutomationId = "Password.Cancel" };
        cancel.Clicked += (_, _) => _ = CloseAsync(null);

        Content = new VerticalStackLayout
        {
            Padding = 24, Spacing = 16, MaximumWidthRequest = 480, VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = message, AutomationId = "Password.Message" },
                entry,
                new HorizontalStackLayout { Spacing = 12, HorizontalOptions = LayoutOptions.End, Children = { cancel, open } },
            },
        };
        Loaded += (_, _) => entry.Focus();
    }

    /// <summary>Show the prompt; returns the password, or null if the user cancelled.</summary>
    public static async Task<string?> AskAsync(string message)
    {
        var page = new PasswordPage(message);
        await Shell.Current.Navigation.PushModalAsync(page);
        return await page.result.Task;
    }

    // Android back button: same as Cancel.
    protected override bool OnBackButtonPressed()
    {
        _ = CloseAsync(null);
        return true;
    }

    // Pop before completing: a wrong password brings the reader straight back for another prompt, and pushing
    // a modal page while the previous one is still being popped loses the new page.
    private async Task CloseAsync(string? password)
    {
        if (closing) return;
        closing = true;
        try
        {
            await Navigation.PopModalAsync();
        }
        finally
        {
            result.TrySetResult(password);
        }
    }
}
