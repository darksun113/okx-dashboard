using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

// Disambiguate WinForms vs WPF types brought in by UseWindowsForms.
using Brushes = System.Windows.Media.Brushes;
using Orientation = System.Windows.Controls.Orientation;

namespace OKXMonitor.Views.Controls;

/// A vertical pager that shows `RowsPerPage` items per page and auto-advances
/// every 3s, with page dots. Mirrors the macOS OrdersCarousel/WatchlistCarousel.
public sealed class Carousel : ContentControl
{
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3) };
    readonly StackPanel _rows = new();
    readonly StackPanel _dots = new()
    {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
        Margin = new Thickness(0, 2, 2, 0),
    };
    int _page;

    public int RowsPerPage { get; set; } = 2;

    /// Builds a UI element for one item.
    public Func<object, UIElement>? RowFactory { get; set; }

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(Carousel),
            new PropertyMetadata(null, (d, _) => ((Carousel)d).Rebuild()));

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public Carousel()
    {
        var outer = new StackPanel();
        outer.Children.Add(_rows);
        outer.Children.Add(_dots);
        Content = outer;
        _timer.Tick += (_, _) => { _page++; Rebuild(); };
        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
    }

    List<object> Items() => ItemsSource?.Cast<object>().ToList() ?? new();

    int PageCount(int count) => Math.Max(1, (count + RowsPerPage - 1) / RowsPerPage);

    void Rebuild()
    {
        if (RowFactory is null) return;
        _rows.Children.Clear();
        _dots.Children.Clear();

        var items = Items();
        int pages = PageCount(items.Count);
        int page = pages == 0 ? 0 : _page % pages;
        int start = page * RowsPerPage;

        for (int i = start; i < Math.Min(start + RowsPerPage, items.Count); i++)
            _rows.Children.Add(RowFactory(items[i]));

        if (pages > 1)
            for (int p = 0; p < pages; p++)
                _dots.Children.Add(new Ellipse
                {
                    Width = 4, Height = 4, Margin = new Thickness(1.5, 0, 1.5, 0),
                    Fill = Brushes.Gray,
                    Opacity = p == page ? 0.8 : 0.25,
                });
    }
}
