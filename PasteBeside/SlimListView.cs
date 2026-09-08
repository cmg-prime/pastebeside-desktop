namespace PasteBeside;

public class SlimListView : ListView
{
    protected override DependencyObject GetContainerForItemOverride()
        => new SlimListViewItem();

    protected override bool IsItemItsOwnContainerOverride(object item)
        => item is SlimListViewItem;
}