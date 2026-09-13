namespace PasteBeside.PeerToPeer;

public interface MessageService: IDisposable
{
	Task BeginListening();
	Task Send(string message);
}