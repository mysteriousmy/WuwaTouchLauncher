using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using AtomUI.Desktop.Controls;
using Popup = AtomUI.Desktop.Controls.Popup;

// AtomUI's touch release updates the candidate but consumes the click before
// Select commits it. Use Avalonia's tap recognizer so a scroll never commits.
internal sealed class TouchSelect : Select
{
    private Popup? popup;
    internal Control? PopupContent => popup?.Child;
    protected override Type StyleKeyOverride => typeof(Select);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        if (popup is not null)
        {
            popup.RemoveHandler(InputElement.TappedEvent, CommitTap);
            popup.RemoveHandler(PointerReleasedEvent, IgnoreTouchClick);
        }
        base.OnApplyTemplate(e);
        popup = e.NameScope.Find<Popup>("PART_Popup");
        popup?.AddHandler(InputElement.TappedEvent, CommitTap, RoutingStrategies.Bubble, handledEventsToo: true);
        popup?.AddHandler(PointerReleasedEvent, IgnoreTouchClick, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void IgnoreTouchClick(object? sender, PointerReleasedEventArgs e)
    {
        // ListViewItem also raises Clicked after a drag outside its tap bounds.
        // Touch commits belong exclusively to the tap recognizer below.
        if (e.Pointer.Type != PointerType.Mouse && e.InitialPressMouseButton == MouseButton.Left)
            e.Handled = true;
    }

    private void CommitTap(object? sender, TappedEventArgs e)
    {
        if (!IsDropDownOpen || e.Pointer.Type == PointerType.Mouse || e.Source is not Visual source) return;
        var item = source.GetSelfAndVisualAncestors().OfType<AtomUI.Desktop.Controls.ListViewItem>().FirstOrDefault();
        if (item is not { IsEffectivelyEnabled: true } || item.DataContext is not ISelectOption { IsEnabled: true } option)
            return;
        SelectedOption = option;
        SetCurrentValue(IsDropDownOpenProperty, false);
        e.Handled = true;
    }
}
