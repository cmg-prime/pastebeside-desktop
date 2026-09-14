using PasteBeside.PeerToPeer;

namespace PasteBeside;

public record AvailablePeerViewModel 
{
	private readonly string _connectedPeerId;	

	public AvailablePeerViewModel(Peer availablePeer, string localPeerId)
	{
		AvailablePeer = availablePeer;
		_connectedPeerId = localPeerId;
	}

	public Peer AvailablePeer { get; }

	public bool IsCurrentlyConnectedPeer { get { return AvailablePeer.Id == _connectedPeerId; } }

    public Brush IconBrush =>
        AvailablePeer.Id == _connectedPeerId
            ? new SolidColorBrush(Colors.Green)
            : new SolidColorBrush(Colors.Blue);
}