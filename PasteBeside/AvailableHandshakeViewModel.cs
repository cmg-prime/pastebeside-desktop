namespace PasteBeside;

public record AvailableHandshakeViewModel 
{
	private readonly string _localPeerHandshakeId;	

	public AvailableHandshakeViewModel(string availableHandshakeId, string localPeerHandshakeId)
	{
		AvailableHandshakeId = availableHandshakeId;
		_localPeerHandshakeId = localPeerHandshakeId;
	}

	public string AvailableHandshakeId { get; }

	public bool IsCurrentActiveHandshake { get { return AvailableHandshakeId == _localPeerHandshakeId; } }

    public Brush IconBrush =>
        AvailableHandshakeId == _localPeerHandshakeId
            ? new SolidColorBrush(Colors.Green)
            : new SolidColorBrush(Colors.Blue);
}