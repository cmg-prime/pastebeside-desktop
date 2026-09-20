using System.Net;
using System.Net.Sockets;

namespace PasteBeside.PeerToPeer;

public class DataChannel: IDisposable
{
	private readonly OutgoingConnectionCommand _outgoingConnectionCommand;
	private readonly IncomingConnectionCommand _incomingConnectionCommand;
	private readonly ConnectionHandler _connectionHandler;
	private readonly MessageServiceFactory _messageServiceFactory;
	private readonly Lock _disposalLock;
	
	private MessageService? _messageService;
	private bool _hasBeenDisposed;

	public DataChannel(OutgoingConnectionCommand outgoingConnectionCommand, IncomingConnectionCommand incomingConnectionCommand, ConnectionHandler connectionHandler, MessageServiceFactory messageServiceFactory)
	{
		_outgoingConnectionCommand = outgoingConnectionCommand;
		_incomingConnectionCommand = incomingConnectionCommand;
		_connectionHandler = connectionHandler;
		_messageServiceFactory = messageServiceFactory;
		_disposalLock = new();
	}

	public bool IsConnected => _messageService is not null;

	public async Task MakeIncomingConnection(TcpClient incomingClient, CancellationToken rootCancelToken)
	{
		var client = await _connectionHandler.HandleConnectCommand(
			Connect: (cancelToken) => _incomingConnectionCommand.Execute(incomingClient, cancelToken),
			rootCancelToken
		);
		_messageService = await InitializeMessageChannel(client, rootCancelToken);
	}

	public async Task MakeOutgoingConnection(IPEndPoint endpoint, CancellationToken rootCancelToken)
	{
		var client = await _connectionHandler.HandleConnectCommand(
			Connect: (cancelToken) => _outgoingConnectionCommand.Execute(endpoint, cancelToken),
			rootCancelToken
		);
		_messageService = await InitializeMessageChannel(client, rootCancelToken);
	}

	private async Task<MessageService?> InitializeMessageChannel(TcpClient? client, CancellationToken cancelToken)
	{
		if(client is null)
			return null;

		var messageService = _messageServiceFactory.Create(client, cancelToken);
		try
		{
			await messageService.BeginListening();
			// NB: if we intentionally start a new connection while holding an existing connection, we
			// need to dispose of the existing resources as best we can (ideally without blocking the lock).
			Action? DisposePreviousService = null;
			lock (_disposalLock)
			{
				// NB: have to guard here: the class may have been disposed while we awaited e.g. BeginListening.
				if (_hasBeenDisposed)
				{
					messageService.Dispose();
				}
				else
				{
					DisposePreviousService = _messageService is not null ? _messageService.Dispose : null;
				}
			}	
			DisposePreviousService?.Invoke();
		}
		catch
		{
			messageService.Dispose();
			throw;
		}

		return messageService;
	}

	public void Dispose()
	{
		Action? OnDisconnected;
		lock (_disposalLock)
		{
			OnDisconnected = _messageService is not null ? _messageService.Dispose : null;	
			_hasBeenDisposed = true;		
		}
		OnDisconnected?.Invoke();
	}

}