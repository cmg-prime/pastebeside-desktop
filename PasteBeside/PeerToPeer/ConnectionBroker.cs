using System.Net;
using System.Net.Sockets;

namespace PasteBeside.PeerToPeer;

public class ConnectionBroker
{
	private readonly OutgoingConnectionCommand _outgoingConnectionCommand;
	private readonly IncomingConnectionCommand _incomingConnectionCommand;
	private readonly ConnectionHandler _connectionHandler;

	private MessageService? _messageService;

	public ConnectionBroker(OutgoingConnectionCommand outgoingConnectionCommand, IncomingConnectionCommand incomingConnectionCommand, ConnectionHandler connectionHandler)
	{
		_outgoingConnectionCommand = outgoingConnectionCommand;
		_incomingConnectionCommand = incomingConnectionCommand;
		_connectionHandler = connectionHandler;
	}

	public bool IsConnected => _messageService is not null;

	public async Task MakeIncomingConnection(TcpClient incomingClient, CancellationToken rootCancelToken)
	{
		_messageService = await _connectionHandler.HandleConnectCommand(
			Connect: (cancelToken) => _incomingConnectionCommand.Execute(incomingClient, cancelToken),
			rootCancelToken
		);
	}

	public async Task MakeOutgoingConnection(IPEndPoint endpoint, CancellationToken rootCancelToken)
	{
		_messageService = await _connectionHandler.HandleConnectCommand(
			Connect: (cancelToken) => _outgoingConnectionCommand.Execute(endpoint, cancelToken),
			rootCancelToken
		);
	}

}