using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DynamicIsland;

/// <summary>Полка: перетащи файлы на остров, потом вытащи их куда нужно.</summary>
public partial class MainWindow
{
    readonly List<string> _shelf = ShelfStore.Load();
    bool _dragActive;
    DateTime _dragLastSeen;
    bool _draggingOut;

    void RenderShelf()
    {
        ShelfItems.Children.Clear();
        foreach (var path in _shelf) ShelfItems.Children.Add(CreateShelfItem(path));

        bool empty = _shelf.Count == 0;
        ShelfHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        ShelfDrop.Visibility = empty || _dragActive ? Visibility.Visible : Visibility.Collapsed;
        ShelfDrop.Stroke = new SolidColorBrush(_dragActive ? Color.FromRgb(0x64, 0xD2, 0xFF) : Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
        ShelfClear.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        ShelfCount.Text = empty ? "" : $"{_shelf.Count}";
    }

    FrameworkElement CreateShelfItem(string path)
    {
        var name = Path.GetFileName(path.TrimEnd('\\'));
        var item = new StackPanel
        {
            Width = 68,
            Margin = new Thickness(0, 0, 6, 0),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = path,
        };
        item.Children.Add(new Image { Width = 40, Height = 40, Source = ShelfStore.GetIcon(path), Stretch = Stretch.Uniform });
        item.Children.Add(new TextBlock
        {
            Text = string.IsNullOrEmpty(name) ? path : name,
            FontSize = 11,
            Margin = new Thickness(0, 5, 0, 0),
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = new SolidColorBrush(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF)),
        });

        Point? down = null;
        item.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            if (e.ClickCount == 2)
            {
                down = null;
                OpenPath(path);
                return;
            }
            down = e.GetPosition(item);
        };
        item.MouseLeftButtonUp += (_, e) =>
        {
            down = null;
            e.Handled = true;
        };
        item.MouseMove += (_, e) =>
        {
            if (down is not { } start || e.LeftButton != MouseButtonState.Pressed) return;
            if ((e.GetPosition(item) - start).Length < 6) return;
            down = null;
            DragOut(item, path);
        };

        var menu = new ContextMenu();
        menu.Items.Add(MenuItem("Открыть", () => OpenPath(path)));
        menu.Items.Add(MenuItem("Показать в папке", () =>
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true })));
        menu.Items.Add(MenuItem("Убрать с полки", () => RemoveFromShelf(path)));
        item.ContextMenu = menu;
        return item;
    }

    static MenuItem MenuItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Log.Write("shelf " + header, ex);
            }
        };
        return item;
    }

    void DragOut(FrameworkElement source, string path)
    {
        if (!ShelfStore.Exists(path))
        {
            RemoveFromShelf(path);
            return;
        }
        _draggingOut = true;
        try
        {
            var data = new DataObject(DataFormats.FileDrop, new[] { path });
            var result = DragDrop.DoDragDrop(source, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
            if (result.HasFlag(DragDropEffects.Move) && !ShelfStore.Exists(path)) RemoveFromShelf(path);
        }
        finally
        {
            _draggingOut = false;
        }
    }

    static void OpenPath(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Write("open " + path, ex);
        }
    }

    void RemoveFromShelf(string path)
    {
        _shelf.Remove(path);
        ShelfStore.Save(_shelf);
        RenderShelf();
    }

    void ShelfClear_Click(object sender, RoutedEventArgs e)
    {
        _shelf.Clear();
        ShelfStore.Save(_shelf);
        RenderShelf();
    }

    void Island_DragEnter(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (_draggingOut || !e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.None;
            return;
        }
        e.Effects = DragDropEffects.Copy;
        _dragLastSeen = DateTime.Now;
        if (_dragActive) return;
        _dragActive = true;
        _page = IslandPage.Shelf;
        RenderShelf();
        UpdateIsland();
    }

    void Island_DragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        bool accept = !_draggingOut && e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = accept ? DragDropEffects.Copy : DragDropEffects.None;
        if (accept) _dragLastSeen = DateTime.Now;
    }

    void Island_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (_draggingOut) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            foreach (var file in files)
                if (!_shelf.Contains(file, StringComparer.OrdinalIgnoreCase)) _shelf.Add(file);
            ShelfStore.Save(_shelf);
        }
        EndDrag();
        _page = IslandPage.Shelf;
        _pinnedUntil = DateTime.Now.AddSeconds(2.5);
        UpdateIsland();
    }

    void EndDrag()
    {
        _dragActive = false;
        RenderShelf();
    }
}
