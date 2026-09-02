namespace PasteBeside;

public class CursorListView : ListView
{
    protected override DependencyObject GetContainerForItemOverride()
        => new CursorListViewItem();

    protected override bool IsItemItsOwnContainerOverride(object item)
        => item is CursorListViewItem;
}