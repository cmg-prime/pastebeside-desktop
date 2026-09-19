using System.Net.Sockets;
using PasteBeside.ClientFriendlyLog;
using PasteBeside.Eventing;

namespace PasteBeside.PeerToPeer;

public class ConnectionHandler: IDisposable
{
	private readonly ClientLogger _logger;
	private readonly EventBus _eventBus;
	private readonly MessageServiceFactory _messageServiceFactory;
	private readonly LastConnectedPeerRepository _lastConnectedPeerRepository;
	private readonly DiscoveredPeerRepository _discoveryRepository;
	private readonly ReconnectService _reconnectService;
	private readonly SemaphoreSlim _connectionLock;

	private CancellationTokenSource? _reconnectCancelTokenSource;
	private bool _isDisposed;
	private Action? _OnDisconnected;

	public ConnectionHandler(ClientLogger logger, EventBus eventBus, MessageServiceFactory messageServiceFactory, LastConnectedPeerRepository lastConnectedPeerRepository, DiscoveredPeerRepository discoveryRepository, ReconnectService reconnectService)
	{
		_logger = logger;
		_eventBus = eventBus;
		_connectionLock = new(1,1);
		_messageServiceFactory = messageServiceFactory;
		_lastConnectedPeerRepository = lastConnectedPeerRepository;
		_discoveryRepository = discoveryRepository;
		_reconnectService = reconnectService;
	}
	
	// TODO: instead of this Func, should we pass in some kind of ConnectionCommand?
	public async Task<MessageService?> HandleConnectCommand(Func<CancellationToken, Task<TcpClient?>> Connect, CancellationToken cancelToken)
	{
		await _connectionLock.WaitAsync(cancelToken);
		// NB: an intentional connection takes precedence over an attempted reconnect.
		_reconnectCancelTokenSource?.Cancel();
		_reconnectCancelTokenSource?.Dispose();
		try
		{
			var messageClient = await Connect(cancelToken);
			if(messageClient is null)
				return null;

			return await InitializeMessageChannel(messageClient, cancelToken);
		}
		catch (OperationCanceledException)
		{
			return null;
		}
		catch
		{
			await HandleDisconnect(cancelToken);
			return null;
		}
		finally
		{
			_connectionLock.Release();
		}
	}

	private async Task<MessageService> InitializeMessageChannel(TcpClient client, CancellationToken cancelToken)
	{
		var messageService = _messageServiceFactory.Create(client, cancelToken);
		await messageService.BeginListening();
		_OnDisconnected = messageService.Dispose;

		return messageService;
	}

	private async Task HandleDisconnect(CancellationToken cancelToken)
	{
		_reconnectCancelTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancelToken);
		_OnDisconnected?.Invoke();
		_OnDisconnected = null;
		var lastConnectedPeer = _lastConnectedPeerRepository.Get();
		if(lastConnectedPeer is not null)
		{
			_discoveryRepository.RemoveById(lastConnectedPeer.Id);
			_eventBus.OnPeerDisconnected(lastConnectedPeer.Id);	
		}
		if(!_isDisposed)
			// NB: we want to run reconnects in background.
			// TODO: backgroundservice?
#pragma warning disable CS4014
			await _reconnectService.Reconnect([_reconnectCancelTokenSource.Token]);				
#pragma warning restore CS4014

		await _logger.Error("Disconnected from peer.");
	}

	public void Dispose()
	{
		if (_isDisposed)
			return;

		_isDisposed = true;
		_OnDisconnected?.Invoke();
		_OnDisconnected = null;	
		// TODO: if we find ourselves doing this in other classes, make an extension method.
		_reconnectCancelTokenSource?.Cancel();
		_reconnectCancelTokenSource?.Dispose();
	}
}