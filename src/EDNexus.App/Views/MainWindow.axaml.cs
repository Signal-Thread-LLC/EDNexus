using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using EDNexus.App.Controls;
using EDNexus.App.ViewModels;

namespace EDNexus.App.Views;

public partial class MainWindow : Window
{
    /// <summary>
    /// Drag payload: the id of the card being carried. In-process, because a dashboard card only
    /// means anything to this window — there is nothing to hand another application.
    /// </summary>
    private static readonly DataFormat<string> CardFormat =
        DataFormat.CreateInProcessFormat<string>("ednexus-dashboard-card");

    public MainWindow()
    {
        InitializeComponent();

        // Tunnelling: the press has to be seen before the card's own controls get it.
        AddHandler(PointerPressedEvent, OnCardPointerPressed, RoutingStrategies.Tunnel);

        AddHandler(DragDrop.DragOverEvent, OnCardDragOver);
        AddHandler(DragDrop.DropEvent, OnCardDrop);
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => ClearIndicator());
    }

    /// <summary>
    /// A press on a card's title starts a drag. Only the title: everything else on a card is a
    /// control the commander is trying to use, and a dashboard that moves when you click a button
    /// is worse than one that never moves at all.
    /// </summary>
    private async void OnCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.Source is not Visual source) return;
        if (source is not TextBlock title || !title.Classes.Contains("cardHeader")) return;
        if (CardOf(source) is not { } card) return;

        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(CardFormat, card.Id));

        try
        {
            await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
        }
        finally
        {
            ClearIndicator();
        }
    }

    private void OnCardDragOver(object? sender, DragEventArgs e)
    {
        if (Target(e) is not { } target)
        {
            e.DragEffects = DragDropEffects.None;
            ClearIndicator();
            return;
        }

        e.DragEffects = DragDropEffects.Move;
        ShowIndicator(target.Panel, target.Panel.IndicatorFor(target.Column, target.Before));
    }

    private void OnCardDrop(object? sender, DragEventArgs e)
    {
        ClearIndicator();

        if (e.DataTransfer.TryGetValue(CardFormat) is not { } id) return;
        if (Target(e) is not { } target) return;
        if (DataContext is not MainWindowViewModel vm) return;

        var before = target.Before is null ? null : CardOf(target.Before)?.Id;

        // Snapshot where every card is drawn right now, so the ones still packed automatically stay
        // put instead of re-flowing around this drop.
        var rendered = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in target.Panel.Children)
        {
            if (CardOf(child) is { } card) rendered[card.Id] = target.Panel.RenderedColumn(child);
        }

        vm.PlaceCard(id, target.Column, before, rendered);
    }

    /// <summary>The panel under the pointer and the slot a drop would take, or null when off it.</summary>
    private (MasonryPanel Panel, int Column, Control? Before)? Target(DragEventArgs e)
    {
        if (!e.DataTransfer.Contains(CardFormat)) return null;
        if (CardsPanel is not { } panel) return null;

        var point = e.GetPosition(panel);
        if (point.X < 0 || point.Y < 0 || point.X > panel.Bounds.Width) return null;

        var (column, before) = panel.DropTargetAt(point);
        return (panel, column, before);
    }

    /// <summary>
    /// Float the drop bar over the cards. The rect arrives in panel space, which scrolls; the
    /// overlay does not, so it has to be translated rather than merely offset.
    /// </summary>
    private void ShowIndicator(MasonryPanel panel, Rect bar)
    {
        if (DropLine.Parent is not Visual overlay ||
            panel.TranslatePoint(bar.Position, overlay) is not { } origin)
        {
            ClearIndicator();
            return;
        }

        Canvas.SetLeft(DropLine, origin.X);
        Canvas.SetTop(DropLine, origin.Y);
        DropLine.Width = bar.Width;
        DropLine.Height = bar.Height;
        DropLine.IsVisible = true;
    }

    private void ClearIndicator() => DropLine.IsVisible = false;

    /// <summary>The masonry panel inside the cards ItemsControl, once it has been templated.</summary>
    private MasonryPanel? CardsPanel => CardHost.GetVisualDescendants().OfType<MasonryPanel>().FirstOrDefault();

    /// <summary>Walk out to the card whose template the visual belongs to.</summary>
    private static CardViewModel? CardOf(Visual source)
    {
        for (Visual? v = source; v is not null; v = v.GetVisualParent())
        {
            if (v is ContentPresenter { DataContext: CardViewModel card }) return card;
        }

        return null;
    }
}
