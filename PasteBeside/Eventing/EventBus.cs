using PasteBeside.PeerToPeer;

namespace PasteBeside.Eventing;

public class EventBus
{
	public event EventHandler<PeerEventArgs>? LocalPeerConfigured;
	public event EventHandler<PeerEventArgs>? PeerDiscovered;
	public event EventHandler<PeerEventArgs>? PeerConnected;
	public event EventHandler<PeerIdEventArgs>? PeerDisconnected;

	public void OnLocalPeerConfigured(PeerEventArgs args)
		=> LocalPeerConfigured?.Invoke(this, args);

	public void OnPeerDiscovered(PeerEventArgs args)
		=> PeerDiscovered?.Invoke(this, args);

	public void OnPeerConnected(PeerEventArgs args)
		=> PeerConnected?.Invoke(this, args);

	public void OnPeerDisConnected(PeerIdEventArgs args)
		=> PeerDisconnected?.Invoke(this, args);
}