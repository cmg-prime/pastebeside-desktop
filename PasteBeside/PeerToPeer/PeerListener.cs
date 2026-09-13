namespace PasteBeside.PeerToPeer;

public interface PeerListener: IDisposable
{
	int Port { get; }
}