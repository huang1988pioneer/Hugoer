using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Hugoer.ViewModels;

namespace Hugoer.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Ctrl+K 需要在按下的同時把焦點移到搜尋框，因此用 tunnel 事件先攔下來，
        // 不讓底下的編輯器（AvaloniaEdit / WebView）吃掉按鍵。
        AddHandler(KeyDownEvent, OnShellKeyDown, RoutingStrategies.Tunnel);

        if (this.FindControl<ListBox>("PaletteList") is { } list)
            list.AddHandler(InputElement.TappedEvent, OnPaletteTapped, RoutingStrategies.Bubble);

        if (this.FindControl<Border>("PaletteOverlay") is { } overlay)
            overlay.AddHandler(PointerPressedEvent, OnOverlayPressed, RoutingStrategies.Bubble);
    }

    /// <summary>點面板以外的暗色區域即關閉，不必特地移到某顆關閉鈕。</summary>
    private void OnOverlayPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, sender))
            return;

        if (DataContext is MainViewModel vm)
            vm.ClosePaletteCommand.Execute(null);
    }

    private void OnShellKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.K)
        {
            vm.OpenPaletteCommand.Execute(null);
            FocusPalette();
            e.Handled = true;
            return;
        }

        if (!vm.IsPaletteOpen)
            return;

        switch (e.Key)
        {
            case Key.Escape:
                vm.ClosePaletteCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Down:
                vm.MovePaletteSelection(1);
                e.Handled = true;
                break;

            case Key.Up:
                vm.MovePaletteSelection(-1);
                e.Handled = true;
                break;

            case Key.Enter:
                vm.RunPaletteEntryCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void OnPaletteTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainViewModel { IsPaletteOpen: true } vm)
            vm.RunPaletteEntryCommand.Execute(null);
    }

    private void FocusPalette()
    {
        if (this.FindControl<TextBox>("PaletteBox") is not { } box)
            return;

        Dispatcher.UIThread.Post(
            () =>
            {
                box.Focus();
                box.SelectAll();
            },
            DispatcherPriority.Input);
    }
}
