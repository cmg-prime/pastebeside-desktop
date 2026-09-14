using System.Net;

namespace PasteBeside.PeerToPeer;

[ImplicitKeys(IsEnabled = false)]
public record Peer
{
    public Peer(string id, IPEndPoint endpoint)
    {
        Id = id;
		Endpoint = endpoint;
    }

    public string Id { get; }
	public IPEndPoint Endpoint { get; }
}