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
	private readonly Lock _disposalLock;

	private CancellationTokenSource? _connectCancelTokenSource;
	private CancellationTokenSource? _reconnectCancelTokenSource;
	private bool _hasBeenDisposed;
	private Action? _OnDisconnected;

	public ConnectionHandler(ClientLogger logger, EventBus eventBus, MessageServiceFactory messageServiceFactory, LastConnectedPeerRepository lastConnectedPeerRepository, DiscoveredPeerRepository discoveryRepository, ReconnectService reconnectService)
	{
		_logger = logger;
		_eventBus = eventBus;
		_connectionLock = new(1,1);
		_disposalLock = new();
		_messageServiceFactory = messageServiceFactory;
		_lastConnectedPeerRepository = lastConnectedPeerRepository;
		_discoveryRepository = discoveryRepository;
		_reconnectService = reconnectService;
	}
	
	// TODO: instead of this Func, should we pass in some kind of ConnectionCommand?
	public async Task<MessageService?> HandleConnectCommand(Func<CancellationToken, Task<TcpClient?>> Connect, CancellationToken cancelToken)
	{
		await _connectionLock.WaitAsync(cancelToken);
		var shouldHandleDisconnect = false;
		CancellationTokenSource? cancelTokenSource = null;
		// NB: the class field CTS can be canceled or disposed from another operation while it's still referenced
		// here, so we *also* need a stable reference token (instead of reading from the local CTS).
		CancellationToken? stableCancelToken = null;
		try
		{
			CancellationTokenSource? reconnectCancelTokenSource;
			lock (_disposalLock)
			{
				if (_hasBeenDisposed)
					return null;

				cancelTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancelToken);
				stableCancelToken = cancelTokenSource.Token;
				_connectCancelTokenSource = cancelTokenSource;
				// NB: an intentional connection takes precedence over an attempted reconnect.
				reconnectCancelTokenSource = _reconnectCancelTokenSource;
				_reconnectCancelTokenSource = null;
			}
			reconnectCancelTokenSource?.Cancel();
			reconnectCancelTokenSource?.Dispose();

			var messageClient = await Connect((CancellationToken)stableCancelToken);
			if (messageClient is null)
				return null;

			return await InitializeMessageChannel(messageClient, (CancellationToken)stableCancelToken);
		}
		catch (OperationCanceledException) { }
		catch 
		{ 
			// NB: shouldn't block on reconnect attempt inside the _connectionLock.
			shouldHandleDisconnect = true;
		}
		finally
		{
			lock (_disposalLock)
			{
				// NB: if another operation has set a new _cancelTokenSource, that operation now owns its lifecycle.
				if (ReferenceEquals(_connectCancelTokenSource, cancelTokenSource))
					_connectCancelTokenSource = null;
			}
			cancelTokenSource?.Dispose();
			_connectionLock.Release();
		}

		return shouldHandleDisconnect ? await HandleDisconnect(cancelToken) : null;
	}

	private async Task<MessageService> InitializeMessageChannel(TcpClient client, CancellationToken cancelToken)
	{
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
					DisposePreviousService = _OnDisconnected;
					_OnDisconnected = messageService.Dispose;	
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

	private async Task<MessageService?> HandleDisconnect(CancellationToken cancelToken)
	{
		CancellationTokenSource? reconnectSource = null;
		CancellationToken? reconnectCancelToken = null;
		lock (_disposalLock)
		{
			if(_hasBeenDisposed)
				return null;

			var lastConnectedPeer = _lastConnectedPeerRepository.Get();
			if(lastConnectedPeer is not null)
			{
				_discoveryRepository.RemoveById(lastConnectedPeer.Id);
				_eventBus.OnPeerDisconnected(lastConnectedPeer.Id);	
			}
			if (!_hasBeenDisposed)
			{
				_OnDisconnected?.Invoke();
				_OnDisconnected = null;	
				reconnectSource = CancellationTokenSource.CreateLinkedTokenSource(cancelToken);
				reconnectCancelToken = reconnectSource.Token;
            	_reconnectCancelTokenSource = reconnectSource;
			}
		}

		if(reconnectSource is not null && reconnectCancelToken is not null)
		{
			try
			{
				// NB: the class field can be canceled or disposed from another operation before we need it here,
				// so we use a stable reference instead of reading from the source to avoid clashing state updates.
				var messageClient = await _reconnectService.Reconnect([(CancellationToken)reconnectCancelToken]);
				if(messageClient is null)
					return null;
				
				return await InitializeMessageChannel(messageClient, (CancellationToken)reconnectCancelToken);
			}
			finally
			{
				lock (_disposalLock)
					if (ReferenceEquals(_reconnectCancelTokenSource, reconnectSource))
						_reconnectCancelTokenSource = null;

				reconnectSource?.Dispose();
			}
		}

		await _logger.Error("Disconnected from peer.");
		return null;
	}

	public void Dispose()
	{
		// NB: We want to Dispose the CancellationTokenSource outside the lock, where it can't block
		// or deadlock on potentially registered cancellation callbacks. To do that, we keep a stable
		// reference to the token source at lock time, so we can dispose it even if the class field is 
		// reassigned in the interim.
		CancellationTokenSource? reconnectCancelTokenSource;
		CancellationTokenSource? connectCancelTokenSource;
		Action? OnDisconnected;
		lock (_disposalLock)
		{
			if (_hasBeenDisposed)
				return;

			_hasBeenDisposed = true;
			reconnectCancelTokenSource = _reconnectCancelTokenSource;
			_reconnectCancelTokenSource = null;
			connectCancelTokenSource = _connectCancelTokenSource;
			_connectCancelTokenSource = null;
			OnDisconnected = _OnDisconnected;
			_OnDisconnected = null;	
		}
		reconnectCancelTokenSource?.Cancel();
		reconnectCancelTokenSource?.Dispose();
		connectCancelTokenSource?.Cancel();
		connectCancelTokenSource?.Dispose();
		OnDisconnected?.Invoke();
		// NB: wait for any extant TryReconnect calls to release the lock before disposing it.
		_connectionLock.Wait();
		_connectionLock.Release();
	}
}