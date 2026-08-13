namespace PasteBeside;

internal partial record MainModel
{
	public MainModel()
	{
		ClientLog = State.Value(this, () => new ClientLog());
	}

	public IState<ClientLog> ClientLog { get; }
}
