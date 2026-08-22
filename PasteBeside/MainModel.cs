using PasteBeside.ClientFriendlyLog;

namespace PasteBeside;

internal partial record MainModel
{
	public MainModel(ClientLog clientLog)
	{
		ClientLog = State.Value(this, () => clientLog);
	}

	public IState<ClientLog> ClientLog { get; }
}
