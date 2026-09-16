using System.Net;

namespace PasteBeside.PeerToPeer;

public class PeerRepository
{
	private readonly IList<Peer> _peers = [];

	public void AddPeer(Peer peer) => _peers.Add(peer);

	public Peer? SearchById(string peerIdentifier)
		=> _peers.FirstOrDefault(peer => peer.Id.Equals(peerIdentifier));

	public void RemoveById(string peerId)
	{
		var targetPeer = _peers.First(peer => peer.Id.Equals(peerId));
		_peers.Remove(targetPeer);
	}
}