using Celbridge.Code.Views;
using Microsoft.Extensions.Localization;

namespace Celbridge.Markdown.Views;

/// <summary>
/// Creates the Markdown snippet insertion toolbar for the CodeEditorDocumentView.
/// The toolbar contains a button with a flyout menu of common Markdown snippets.
/// </summary>
public static class MarkdownSnippetToolbar
{
    private const string BoldSnippet = "**bold text**";
    private const string ItalicSnippet = "*italic text*";
    private const string StrikethroughSnippet = "~~strikethrough text~~";
    private const string UnorderedListSnippet = "- Item 1\n- Item 2\n- Item 3\n";
    private const string OrderedListSnippet = "1. Item 1\n2. Item 2\n3. Item 3\n";
    private const string TaskListSnippet = "- [ ] Task 1\n- [ ] Task 2\n- [x] Completed task\n";
    private const string CodeBlockSnippet = "```language\ncode here\n```\n";
    private const string BlockquoteSnippet = "> Quoted text here\n";
    private const string LinkSnippet = "[title](https://example.com)";
    private const string ImageSnippet = "![alt text](image.png)";
    private const string TableSnippet = "| Header 1 | Header 2 | Header 3 |\n| -------- | -------- | -------- |\n| Cell     | Cell     | Cell     |\n| Cell     | Cell     | Cell     |\n";
    private const string FootnoteSnippet = "Here is a footnote reference.[^1]\n\n[^1]: Footnote text here.\n";
    private const string HorizontalRuleSnippet = "\n---\n";

    /// <summary>
    /// Creates the snippet toolbar button with a flyout menu.
    /// Automatically disables when the view is in Preview mode.
    /// </summary>
    public static UIElement Create(CodeEditorDocumentView view, IStringLocalizer stringLocalizer)
    {
        var insertSnippetTooltip = stringLocalizer.GetString("Markdown_Snippet_Insert");

        var button = new Button
        {
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            Content = new FontIcon
            {
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                Glyph = "\uE710",
                FontSize = 14
            }
        };

        ToolTipService.SetToolTip(button, insertSnippetTooltip);
        ToolTipService.SetPlacement(button, Microsoft.UI.Xaml.Controls.Primitives.PlacementMode.Bottom);

        var menuFlyout = new MenuFlyout();
        AddItem(menuFlyout, view, stringLocalizer, "Markdown_Snippet_Bold", BoldSnippet);
        AddItem(menuFlyout, view, stringLocalizer, "Markdown_Snippet_Italic", ItalicSnippet);
        AddItem(menuFlyout, view, stringLocalizer, "Markdown_Snippet_Strikethrough", StrikethroughSnippet);
        menuFlyout.Items.Add(new MenuFlyoutSeparator());
        AddItem(menuFlyout, view, stringLocalizer, "Markdown_Snippet_UnorderedList", UnorderedListSnippet);
        AddItem(menuFlyout, view, stringLocalizer, "Markdown_Snippet_OrderedList", OrderedListSnippet);
        AddItem(menuFlyout, view, stringLocalizer, "Markdown_Snippet_TaskList", TaskListSnippet);
        menuFlyout.Items.Add(new MenuFlyoutSeparator());
        AddItem(menuFlyout, view, stringLocalizer, "Markdown_Snippet_CodeBlock", CodeBlockSnippet);
        AddItem(menuFlyout, view, stringLocalizer, "Markdown_Snippet_Blockquote", BlockquoteSnippet);
        menuFlyout.Items.Add(new MenuFlyoutSeparator());
        AddItem(menuFlyout, view, stringLocalizer, "Markdown_Snippet_Link", LinkSnippet);
        AddItem(menuFlyout, view, stringLocalizer, "Markdown_Snippet_Image", ImageSnippet);
        menuFlyout.Items.Add(new MenuFlyoutSeparator());
        AddItem(menuFlyout, view, stringLocalizer, "Markdown_Snippet_Table", TableSnippet);
        AddItem(menuFlyout, view, stringLocalizer, "Markdown_Snippet_Footnote", FootnoteSnippet);
        AddItem(menuFlyout, view, stringLocalizer, "Markdown_Snippet_HorizontalRule", HorizontalRuleSnippet);

        button.Flyout = menuFlyout;

        // Disable the snippet button when in Preview mode (no editor to insert into)
        view.ViewModeChanged += (_, viewMode) =>
        {
            button.IsEnabled = viewMode != SplitEditorViewMode.Preview;
        };

        return button;
    }

    private static void AddItem(
        MenuFlyout flyout,
        CodeEditorDocumentView view,
        IStringLocalizer stringLocalizer,
        string labelKey,
        string snippet)
    {
        var item = new MenuFlyoutItem
        {
            Text = stringLocalizer.GetString(labelKey)
        };

        item.Click += async (_, _) =>
        {
            await view.InsertTextAtCaretAsync(snippet);
        };

        flyout.Items.Add(item);
    }
}
