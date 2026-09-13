namespace PasteBeside.PeerToPeer;

public interface DiscoveryService: IDisposable
{
	Task BeginDiscovery();
}