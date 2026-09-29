namespace PasteBeside.ClientFriendlyLog;

public record ClientLogger
{
	public IState<IReadOnlyList<ClientLogMessage>> Messages { get; }

	public ClientLogger()
	{
		Messages = State<IReadOnlyList<ClientLogMessage>>
			.Value(this, () => [new ClientLogMessage("PasteBeside started!", ClientLogType.Success, DateTime.Now)]);
	}

#pragma warning disable CS4014
	public void Info(string message) => InfoAsync(message).ConfigureAwait(false);
	public void Error(string message) => ErrorAsync(message).ConfigureAwait(false);
	public void Success(string message) => SuccessAsync(message).ConfigureAwait(false);
#pragma warning restore CS4014

	public async Task InfoAsync(string message)
		=> await Update(message).ConfigureAwait(false);

	public async Task ErrorAsync(string message)
		=> await Update(message, ClientLogType.Error).ConfigureAwait(false);

	public async Task SuccessAsync(string message) 
		=> await Update(message, ClientLogType.Success).ConfigureAwait(false);

	private async ValueTask Update(string message, ClientLogType type = ClientLogType.Info)
		=> await Messages.UpdateAsync(current => [..current!, new ClientLogMessage(message, type, DateTime.Now)]).ConfigureAwait(false);

}