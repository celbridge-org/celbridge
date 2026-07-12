using Celbridge.Dialog;
using Celbridge.UserInterface.Views;
using Celbridge.Validators;

using FocusManager = Microsoft.UI.Xaml.Input.FocusManager;

namespace Celbridge.UserInterface.Services.Dialogs;

public class DialogFactory : IDialogFactory
{
    public IAlertDialog CreateAlertDialog(string titleText, string messageText)
    {
        var dialog = new AlertDialog
        {
            TitleText = titleText,
            MessageText = messageText
        };

        return WithFocusGuard(dialog);
    }

    public IConfirmationDialog CreateConfirmationDialog(string titleText, string messageText, string? primaryButtonText = null, string? secondaryButtonText = null)
    {
        var dialog = new ConfirmationDialog
        {
            TitleText = titleText,
            MessageText = messageText
        };

        // Only override the default button text if custom text is provided
        if (primaryButtonText is not null)
        {
            dialog.PrimaryButtonText = primaryButtonText;
        }

        if (secondaryButtonText is not null)
        {
            dialog.SecondaryButtonText = secondaryButtonText;
        }

        return WithFocusGuard(dialog);
    }

    public IProgressDialog CreateProgressDialog()
    {
        var dialog = new ProgressDialog();
        return WithFocusGuard(dialog);
    }

    public INewProjectDialog CreateNewProjectDialog()
    {
        var dialog = new NewProjectDialog();
        return WithFocusGuard(dialog);
    }

    public IInputTextDialog CreateInputTextDialog(string titleText, string messageText, string defaultText, Range selectionRange, IValidator validator, string? submitButtonKey = null)
    {
        var dialog = new InputTextDialog
        {
            TitleText = titleText,
            HeaderText = messageText,
        };

        if (submitButtonKey is not null)
        {
            dialog.SubmitButtonKey = submitButtonKey;
        }

        dialog.ViewModel.Validator = validator;
        dialog.SetDefaultText(defaultText, selectionRange);

        return WithFocusGuard(dialog);
    }

    public ISecretInputDialog CreateSecretInputDialog(string titleText, string headerText, string? submitButtonKey = null)
    {
        var dialog = new SecretInputDialog
        {
            TitleText = titleText,
            HeaderText = headerText,
        };

        if (submitButtonKey is not null)
        {
            dialog.SubmitButtonKey = submitButtonKey;
        }

        return WithFocusGuard(dialog);
    }

    public INewFileDialog CreateNewFileDialog(string defaultFileName, Range selectionRange, IValidator validator)
    {
        var dialog = new NewFileDialog();

        dialog.ViewModel.Validator = validator;
        dialog.SetDefaultFileName(defaultFileName, selectionRange);

        return WithFocusGuard(dialog);
    }

    public IResourcePickerDialog CreateResourcePickerDialog(IReadOnlyList<string> extensions, string? title = null, bool showPreview = false)
    {
        var dialog = new ResourcePickerDialog();
        dialog.ViewModel.Initialize(extensions, showPreview);
        if (title is not null)
        {
            dialog.SetTitle(title);
        }
        return WithFocusGuard(dialog);
    }

    public IChoiceDialog CreateChoiceDialog(string titleText, string messageText, IReadOnlyList<string> options, int defaultIndex = 0, ChoiceDialogCheckbox? checkbox = null, string? primaryButtonText = null, string? secondaryButtonText = null)
    {
        var dialog = new ChoiceDialog();
        dialog.Initialize(titleText, messageText, options, defaultIndex, checkbox);

        // Only override the default button text if custom text is provided
        if (primaryButtonText is not null)
        {
            dialog.PrimaryButtonText = primaryButtonText;
        }

        if (secondaryButtonText is not null)
        {
            dialog.SecondaryButtonText = secondaryButtonText;
        }

        return WithFocusGuard(dialog);
    }

    // Attaches the shared focus guard to every dialog this factory creates and returns the dialog. Centralised
    // here because the factory is the single place dialogs are constructed, so no dialog author has to remember
    // to wire up focus handling for a new dialog.
    private static T WithFocusGuard<T>(T dialog) where T : ContentDialog
    {
        dialog.Opened += (sender, args) => EnsureInitialFocus(sender);
        return dialog;
    }

    // A dialog launched from a MenuFlyout races the closing flyout's asynchronous focus restoration on the
    // Skia heads, which can leave keyboard focus outside the dialog so the next key press is handled by the
    // panel beneath it. When a dialog opens without having placed focus on one of its own controls, move
    // focus to its first focusable element so the dialog reliably owns the keyboard. Dialogs that focus a
    // specific control themselves are left untouched.
    private static void EnsureInitialFocus(ContentDialog dialog)
    {
        if (dialog.XamlRoot is null)
        {
            return;
        }

        var focusedElement = FocusManager.GetFocusedElement(dialog.XamlRoot) as DependencyObject;
        bool focusWithinDialog = focusedElement is not null
            && VisualTree.GetAncestors(focusedElement, includeSelf: true).Contains(dialog);
        if (focusWithinDialog)
        {
            return;
        }

        // FindNextElementOptions.SearchRoot only supports the directional navigation directions
        // (Up/Down/Left/Right), not Next/Previous, so scoping a Next move to the dialog throws. Find the
        // dialog's first focusable element directly and focus it instead of tabbing into it.
        if (FocusManager.FindFirstFocusableElement(dialog) is Control firstFocusable)
        {
            firstFocusable.Focus(FocusState.Programmatic);
        }
    }
}

