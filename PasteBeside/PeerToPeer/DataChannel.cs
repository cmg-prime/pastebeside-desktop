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

	public async Task<bool> MakeIncomingConnection(TcpClient incomingClient, CancellationToken rootCancelToken)
	{
		// NB: if we were already connected, don't boot the current peer in favor of the new one.
		lock(_disposeLock)
			if(_hasBeenDisposed || IsConnected)
				return false;

		var client = await _connectionHandler.HandleConnectCommand(
			Connect: (cancelToken) => _incomingConnectionCommand.Execute(incomingClient, cancelToken),
			rootCancelToken
		);

		lock(_disposeLock)
			if(IsConnected)
				return false;
		
		return await InitializeMessageChannel(client, rootCancelToken);
	}

	public async Task<bool> MakeOutgoingConnection(IPEndPoint endpoint, CancellationToken rootCancelToken)
	{
		lock(_disposeLock)
			if(_hasBeenDisposed)
				return false;

		var client = await _connectionHandler.HandleConnectCommand(
			Connect: (cancelToken) => _outgoingConnectionCommand.Execute(endpoint, cancelToken),
			rootCancelToken
		);
		return await InitializeMessageChannel(client, rootCancelToken);
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

	private async Task<bool> InitializeMessageChannel(TcpClient? client, CancellationToken cancelToken)
	{
		lock(_disposeLock)
			if(client is null || _hasBeenDisposed)
				return false;

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
		{
			if (_hasBeenDisposed)
			{
				_messageService = null;
				return false;
			}
			_messageService = messageService;
			return true;
		}
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