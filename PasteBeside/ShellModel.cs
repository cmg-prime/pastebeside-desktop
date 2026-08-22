namespace PasteBeside;

public partial record ShellModel
{
	private readonly INavigator _navigator;

	public ShellModel(INavigator navigator)
		=> _navigator = navigator;
}