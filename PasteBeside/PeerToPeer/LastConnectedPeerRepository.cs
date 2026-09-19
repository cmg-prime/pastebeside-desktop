namespace PasteBeside.PeerToPeer;

public class LastConnectedPeerRepository
{
	private Peer? _peer;

	public void Set(Peer peer) => _peer = peer;

	public Peer? Get() => _peer;

	public void Reset() => _peer = null;
}