using PasteBeside.PeerToPeer;

namespace PasteBeside.Eventing;

public class PeerIdEventArgs: EventArgs
{
	public PeerIdEventArgs(string peerId)
		=> PeerId = peerId;

	public string PeerId { get; }

	public static implicit operator PeerIdEventArgs(string peerId) => new(peerId);
}

public class PeerEventArgs: EventArgs
{
	public PeerEventArgs(Peer peer)
		=> Peer = peer;

	public Peer Peer { get; }

	public static implicit operator PeerEventArgs(Peer peer) => new(peer);
}