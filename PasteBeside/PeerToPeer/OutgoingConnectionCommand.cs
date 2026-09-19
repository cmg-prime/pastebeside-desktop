using System.Net;
using System.Net.Sockets;
using PasteBeside.ClientFriendlyLog;
using PasteBeside.Eventing;

namespace PasteBeside.PeerToPeer;
public class OutgoingConnectionCommand: ConnectionCommand
{
	public OutgoingConnectionCommand(ClientLogger logger, EventBus eventBus, HandshakeService handshakeService, LastConnectedPeerRepository lastConnectedPeerRepo, DiscoveredPeerRepository discoveryRepo)
		: base(logger, eventBus, handshakeService, lastConnectedPeerRepo, discoveryRepo)
	{ }

	public async Task<TcpClient?> Execute(IPEndPoint endpoint, CancellationToken cancelToken)
	{
		TcpClient? handshakeClient = new();
		try
		{
			await handshakeClient.ConnectAsync(endpoint.Address, endpoint.Port, cancelToken);
			var succeeded = await Execute("Outgoing connection", handshakeClient, cancelToken);
			if(!succeeded)
				return null;

			// NB: we own the handshake client - and manage its lifecycle - but once the handshake succeeds,
			// what we have is the messaging client the calling code wants to own. So we disengage.
			var messagingClient = handshakeClient;
			handshakeClient = null;
			return messagingClient;
		}
		finally
		{
			handshakeClient?.Close();
		}
	}

}