namespace PasteBeside;

using Microsoft.UI.Input;

public class SlimListViewItem : ListViewItem
{
	public SlimListViewItem()
	{
		MinHeight = 0;
	}

	protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

		// NB: this multi-select drag-and-drop UI element is invisible by default (opacity 0), but
		// not collapsed (and comparatively large): its actual height is what the the ControlTemplate's root
		// Grid (sizing itself to the max of its children by default) sizes itself to, resulting in extraneous 
		// whitespace around the CursorListViewItems we care about.
        if (GetTemplateChild("MultiArrangeOverlayText") is FrameworkElement overlayText)
			overlayText.Visibility = Visibility.Collapsed;
    }
}