namespace PasteBeside.Eventing;

public class MessageEventArgs: EventArgs
{
	public MessageEventArgs(string message)
		=> Message = message;

	public string Message { get; }

	public static implicit operator MessageEventArgs(string message) => new(message);
}