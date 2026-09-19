using System.Net.Sockets;
using PasteBeside.ClientFriendlyLog;
using PasteBeside.Eventing;

namespace PasteBeside.PeerToPeer;

public class IncomingConnectionCommand: ConnectionCommand
{

	public IncomingConnectionCommand(ClientLogger logger, EventBus eventBus, HandshakeService handshakeService, LastConnectedPeerRepository lastConnectedPeerRepo, DiscoveredPeerRepository discoveryRepo)
		: base(logger, eventBus, handshakeService, lastConnectedPeerRepo, discoveryRepo)
	{ }

	public async Task<TcpClient?> Execute(TcpClient incomingClient, CancellationToken cancelToken)
	{
		var succeeded = await Execute("Incoming connection", incomingClient, cancelToken);
		return succeeded ? incomingClient : null;
	}

}