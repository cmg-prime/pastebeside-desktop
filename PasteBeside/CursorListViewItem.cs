namespace PasteBeside;

using Microsoft.UI.Input;

public class CursorListViewItem : SlimListViewItem
{
	public CursorListViewItem()
	{
		ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
	}

}