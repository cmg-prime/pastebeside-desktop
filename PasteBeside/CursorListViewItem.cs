namespace PasteBeside;

using Microsoft.UI.Input;

public class CursorListViewItem : ListViewItem
{
	public CursorListViewItem()
	{
		ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
	}
}