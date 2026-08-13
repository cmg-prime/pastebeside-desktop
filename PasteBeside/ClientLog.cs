namespace PasteBeside;

public record ClientLog
{
	private IList<ClientLogMessage> _messages = [];
	public IReadOnlyList<ClientLogMessage> Messages { get { return [.._messages]; } }

	public ClientLog()
	{
		_messages = [new ClientLogMessage("PasteBeside started!", ClientLogType.Success, DateTime.Now)];
	}

	public ClientLog Info(string message) => Initialize(message);
	public ClientLog Error(string message) => Initialize(message, ClientLogType.Error);
	public ClientLog Success(string message) => Initialize(message, ClientLogType.Success);

	private ClientLog Initialize(string message, ClientLogType type = ClientLogType.Info) 
		=> this with { _messages = [..Messages, new ClientLogMessage(message, type, DateTime.Now) ] };
}