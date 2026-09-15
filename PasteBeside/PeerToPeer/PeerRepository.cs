using System.Net;

namespace PasteBeside.PeerToPeer;

public class PeerRepository
{
	private readonly IList<Peer> _peers = [];

	public void AddPeer(Peer peer) => _peers.Add(peer);

	public Peer FindByEndpoint(IPEndPoint endpoint)
	{
		var targetPeer = _peers.First(peer => peer.Endpoint.Equals(endpoint));
		return targetPeer;
	}

	public void RemoveById(string peerId)
	{
		var targetPeer = _peers.First(peer => peer.Id.Equals(peerId));
		_peers.Remove(targetPeer);
	}
}