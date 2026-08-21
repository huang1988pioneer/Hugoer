using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Hugoer.Services;
using Hugoer.ViewModels;

namespace Hugoer.Views;

public partial class ContentView : UserControl
{
    public ContentView()
    {
        InitializeComponent();
    }

    private void Bold_OnClick(object? sender, RoutedEventArgs e) =>
        ApplyEdit((text, start, length) => MarkdownEditingService.Wrap(text, start, length, "**", "**", "粗體文字"));

    private void Italic_OnClick(object? sender, RoutedEventArgs e) =>
        ApplyEdit((text, start, length) => MarkdownEditingService.Wrap(text, start, length, "*", "*", "斜體文字"));

    private void Strike_OnClick(object? sender, RoutedEventArgs e) =>
        ApplyEdit((text, start, length) => MarkdownEditingService.Wrap(text, start, length, "~~", "~~", "刪除文字"));

    private void InlineCode_OnClick(object? sender, RoutedEventArgs e) =>
        ApplyEdit((text, start, length) => MarkdownEditingService.Wrap(text, start, length, "`", "`", "程式碼"));

    private void Link_OnClick(object? sender, RoutedEventArgs e) =>
        ApplyEdit((text, start, length) => MarkdownEditingService.Link(text, start, length, image: false));

    private void Image_OnClick(object? sender, RoutedEventArgs e) =>
        ApplyEdit((text, start, length) => MarkdownEditingService.Link(text, start, length, image: true));

    private void Heading1_OnClick(object? sender, RoutedEventArgs e) => ApplyHeading(1);
    private void Heading2_OnClick(object? sender, RoutedEventArgs e) => ApplyHeading(2);
    private void Heading3_OnClick(object? sender, RoutedEventArgs e) => ApplyHeading(3);

    private void ApplyHeading(int level) =>
        ApplyEdit((text, start, length) => MarkdownEditingService.Heading(text, start, length, level));

    private void Quote_OnClick(object? sender, RoutedEventArgs e) =>
        ApplyEdit((text, start, length) => MarkdownEditingService.PrefixLines(text, start, length, "> "));

    private void BulletList_OnClick(object? sender, RoutedEventArgs e) =>
        ApplyEdit((text, start, length) => MarkdownEditingService.PrefixLines(text, start, length, "- "));

    private void TaskList_OnClick(object? sender, RoutedEventArgs e) =>
        ApplyEdit((text, start, length) => MarkdownEditingService.PrefixLines(text, start, length, "- [ ] "));

    private void OrderedList_OnClick(object? sender, RoutedEventArgs e) =>
        ApplyEdit(MarkdownEditingService.OrderedList);

    private void CodeBlock_OnClick(object? sender, RoutedEventArgs e) =>
        ApplyEdit(MarkdownEditingService.CodeBlock);

    private void Table_OnClick(object? sender, RoutedEventArgs e) =>
        ApplyEdit(MarkdownEditingService.InsertTable);

    private void HorizontalRule_OnClick(object? sender, RoutedEventArgs e) =>
        ApplyEdit(MarkdownEditingService.HorizontalRule);

    private void MarkdownEditor_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        switch (e.Key)
        {
            case Key.B:
                Bold_OnClick(sender, e);
                e.Handled = true;
                break;
            case Key.I:
                Italic_OnClick(sender, e);
                e.Handled = true;
                break;
            case Key.K:
                Link_OnClick(sender, e);
                e.Handled = true;
                break;
            case Key.S when DataContext is ContentViewModel viewModel:
                if (viewModel.SaveCommand.CanExecute(null))
                    viewModel.SaveCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void ApplyEdit(Func<string, int, int, MarkdownEditResult> operation)
    {
        var text = MarkdownEditor.Text ?? string.Empty;
        var start = Math.Min(MarkdownEditor.SelectionStart, MarkdownEditor.SelectionEnd);
        var length = Math.Abs(MarkdownEditor.SelectionEnd - MarkdownEditor.SelectionStart);
        if (MarkdownEditingService.IsInsideFrontMatter(text, start))
        {
            if (DataContext is ContentViewModel viewModel)
                viewModel.StatusMessage = "請使用上方欄位編輯 front matter；格式工具只作用於 Markdown 正文。";
            return;
        }

        var result = operation(text, start, length);
        if (DataContext is ContentViewModel editorViewModel)
            editorViewModel.EditorText = result.Text;
        else
            MarkdownEditor.Text = result.Text;
        Dispatcher.UIThread.Post(() =>
        {
            MarkdownEditor.Focus();
            MarkdownEditor.SelectionStart = result.SelectionStart;
            MarkdownEditor.SelectionEnd = result.SelectionStart + result.SelectionLength;
        });
    }
}
