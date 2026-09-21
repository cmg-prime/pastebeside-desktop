using System.Net;
using System.Net.Sockets;
using PasteBeside.ClientFriendlyLog;

namespace PasteBeside.PeerToPeer;

public class DataChannel: IDisposable
{
	private readonly ClientLogger _logger;
	private readonly OutgoingConnectionCommand _outgoingConnectionCommand;
	private readonly IncomingConnectionCommand _incomingConnectionCommand;
	private readonly ConnectionHandler _connectionHandler;
	private readonly MessageServiceFactory _messageServiceFactory;
	private readonly Lock _disposeLock;
	
	private MessageService? _messageService;
	private bool _hasBeenDisposed;

	public DataChannel(ClientLogger logger, OutgoingConnectionCommand outgoingConnectionCommand, IncomingConnectionCommand incomingConnectionCommand, ConnectionHandler connectionHandler, MessageServiceFactory messageServiceFactory)
	{
		_logger = logger;
		_outgoingConnectionCommand = outgoingConnectionCommand;
		_incomingConnectionCommand = incomingConnectionCommand;
		_connectionHandler = connectionHandler;
		_messageServiceFactory = messageServiceFactory;
		_disposeLock = new();
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

	public async Task SendMessage(string message)
	{
		Func<string, Task>? Send = null;
		lock (_disposeLock)
		{
			if(_messageService is not null)
				Send = _messageService.Send;
		}

		if(Send is null)
		{
			await _logger.Info("Message broker inoperative. Message not sent.");
			return;	
		}
		await Send!.Invoke(message);
	}

	private async Task<MessageService?> InitializeMessageChannel(TcpClient? client, CancellationToken cancelToken)
	{
		if(client is null)
			return null;

		var messageService = _messageServiceFactory.Create(client, cancelToken);
		try
		{
#pragma warning disable CS4014
			// NB: run in background?
			messageService.BeginListening();
#pragma warning restore CS4014
			// NB: if we intentionally start a new connection while holding an existing connection, we
			// need to dispose of the existing resources as best we can (ideally without blocking the lock).
			Action? DisposePreviousService = null;
			lock (_disposeLock)
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

		lock (_disposeLock)
			return _hasBeenDisposed ? null : messageService;
	}

	public void Dispose()
	{
		Action? OnDisconnected;
		lock (_disposeLock)
		{
			OnDisconnected = _messageService is not null ? _messageService.Dispose : null;
			_messageService = null;
			_hasBeenDisposed = true;
		}
		OnDisconnected?.Invoke();
	}

}