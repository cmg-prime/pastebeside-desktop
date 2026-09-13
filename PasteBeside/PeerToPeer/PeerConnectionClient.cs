using System.Net;
using System.Net.Sockets;

namespace PasteBeside.PeerToPeer;

public interface PeerConnectionClient: IDisposable
{
	bool IsConnected { get; }

	Task MakeIncomingConnection(TcpClient incomingClient);
	Task MakeOutgoingConnection(IPEndPoint endpoint);
	Task TryReconnect();
}