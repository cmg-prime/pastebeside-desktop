namespace PasteBeside.ClientFriendlyLog;

public record ClientLogger
{
	public IState<IReadOnlyList<ClientLogMessage>> Messages { get; }

	public ClientLogger()
	{
		Messages = State<IReadOnlyList<ClientLogMessage>>
			.Value(this, () => [new ClientLogMessage("PasteBeside started!", ClientLogType.Success, DateTime.Now)]);
	}

	public async ValueTask Info(string message)
		=> await Update(message);

	public async ValueTask Error(string message)
		=> await Update(message, ClientLogType.Error);

	public async ValueTask Success(string message) 
		=> await Update(message, ClientLogType.Success);

	private async ValueTask Update(string message, ClientLogType type = ClientLogType.Info)
		=> await Messages.UpdateAsync(current => [..current!, new ClientLogMessage(message, type, DateTime.Now)]);

}