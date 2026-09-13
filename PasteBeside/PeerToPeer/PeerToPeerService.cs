namespace PasteBeside.PeerToPeer;

public class PeerToPeerService: IDisposable
{
	private readonly DiscoveryService _discoveryService;
	private readonly Action _HandleDispose;

	public PeerToPeerService(DiscoveryServiceFactory discoveryFactory, PeerConnectionClientFactory clientFactory, PeerListenerFactory listenerFactory, MessageServiceFactory messageServiceFactory)
	{
		var rootCancelToken = new CancellationTokenSource().Token;
		var peerClient = clientFactory.Create(
			OnConnected: tcpClient =>
			{
				var messageService = messageServiceFactory.Create(
					tcpClient, 
					OnMessageReceived: message => Console.WriteLine($"Message received: {message}"),
					rootCancelToken
				);
				messageService.BeginListening();

				return messageService.Dispose;
			},
			rootCancelToken
		);
		var listener = listenerFactory.Create(
			OnConnected: incomingTcpClient =>
			{
				// NB: if we were already connected, don't boot the current peer in favor of the new one.
				if (peerClient.IsConnected) {
					incomingTcpClient.Close();
					return;
				}

				peerClient.MakeIncomingConnection(incomingTcpClient);
			},
			rootCancelToken
		);
		_discoveryService = discoveryFactory.Create(
			listener.Port,
			OnPeerDiscovered: endpoint =>
			{
				peerClient.MakeOutgoingConnection(endpoint);	
			},
			rootCancelToken
		);

		_HandleDispose = () =>
		{
			peerClient.Dispose();
			listener.Dispose();
			_discoveryService.Dispose();
		};
	}

	public void BroadcastPeer() => _discoveryService.BeginDiscovery();

	public void Dispose() => _HandleDispose();
}