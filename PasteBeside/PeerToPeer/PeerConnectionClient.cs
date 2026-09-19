using System.Net;
using System.Net.Sockets;
using PasteBeside.ClientFriendlyLog;
using PasteBeside.Eventing;

namespace PasteBeside.PeerToPeer;

public class PeerConnectionClient
{
	private readonly ClientLogger _logger;
	private readonly SemaphoreSlim _connectionLock;
	private readonly SemaphoreSlim _reconnectionLock;
	private readonly MessageServiceFactory _messageServiceFactory;
	private readonly DiscoveredPeerRepository _repository;
	private readonly EventBus _eventBus;
	private readonly HandshakeService _handshakeService;
	private readonly CancellationTokenSource _shutdownSource;

	private Action? _OnDisconnected;
	private TcpClient? _client;
	private Peer? _lastKnownPeer;
	private CancellationToken? _rootToken;
	private CancellationTokenSource? _connectCancelTokenSource;
	private CancellationTokenSource? _reconnectSource;
	private Task? _reconnectTask;
	private bool _disposed;

	public PeerConnectionClient(ClientLogger logger, MessageServiceFactory messageServiceFactory, DiscoveredPeerRepository repository, EventBus eventBus, HandshakeService handshakeService)
	{
		_logger = logger;
		_connectionLock = new SemaphoreSlim(1, 1);
		_reconnectionLock = new SemaphoreSlim(1, 1);
		_messageServiceFactory = messageServiceFactory;
		_repository = repository;
		_eventBus = eventBus;
		_handshakeService = handshakeService;
		_shutdownSource = new CancellationTokenSource();
	}

	public bool IsConnected => _client?.Connected == true;

	public async Task MakeIncomingConnection(TcpClient incomingClient, CancellationToken cancelToken)
	{
		GuaranteeLinkedCancellationTokens(cancelToken);
		await HandleConnectCommand(
			async () =>
			{
				// NB: an intentional connection takes precedence over an attempted reconnect.
				_reconnectSource?.Cancel();
				var (handshakeSucceeded, remotePeerId) = await PerformHandshake(incomingClient, _connectCancelTokenSource!.Token);
				if (!handshakeSucceeded)
					return;
				
				_client = incomingClient;
				_lastKnownPeer = _repository.SearchById(remotePeerId!);

				_eventBus.OnPeerConnected(_lastKnownPeer!);
				await InitializeMessageChannel(_connectCancelTokenSource!.Token);		
			}, 
			_connectCancelTokenSource!.Token
		);
	}

	public async Task MakeOutgoingConnection(IPEndPoint endpoint, CancellationToken cancelToken)
	{
		GuaranteeLinkedCancellationTokens(cancelToken);
		await MakeOutGoingConnection(endpoint, _connectCancelTokenSource!.Token);
	}

	private async Task MakeOutGoingConnection(IPEndPoint endpoint, CancellationToken cancelToken, bool shouldReconnect = true)
	{
		await HandleConnectCommand(
			async () =>
			{
				TcpClient? client = new();
				try
				{
					await client.ConnectAsync(endpoint.Address, endpoint.Port, cancelToken);
					var (handshakeSucceeded, remotePeerId) = await PerformHandshake(client, cancelToken);
					if (!handshakeSucceeded)
						return;

					// NB: we own the local client - and manage its lifecycle - but once the handshake succeeds,
					// we pass the connection to the instance client.
					_client = client;
					client = null;

					_lastKnownPeer = _repository.SearchById(remotePeerId!);
					await _logger.Info("Outgoing peer connection accepted!");
					_eventBus.OnPeerConnected(_lastKnownPeer!);

					await InitializeMessageChannel(cancelToken);
				}
				finally
				{
					client?.Close();
				}
			},
			cancelToken,
			shouldReconnect
		);
	}

	private async Task<(bool IsSuccess, string? RemotePeerId)> PerformHandshake(TcpClient client, CancellationToken cancelToken)
	{
		// NB: we need this for a more robust loopback guard than address/port - determining your canonical
		// address involves a surprising number of edge cases. (E.g. ephemeral ports, multiple network 
		// interfaces - switching between wifi, ethernet, VPN, or some other niche network interface
		// during the same session - or what if your DHCP lease expires while your machine sleeps?)
		var remotePeerId = await _handshakeService.PerformHandshake(client, cancelToken);
		if(remotePeerId is not null)
			return (true, remotePeerId);

		client.Close();
		return (false, null);
	}

	private async Task HandleConnectCommand(Func<Task> Connect, CancellationToken cancelToken, bool shouldReconnect = true)
	{
		await _connectionLock.WaitAsync(cancelToken);
		try
		{
			if (IsConnected) 
				return;

			await Connect();
		}
		catch (OperationCanceledException)
		{
			_client?.Close();
			_client = null;
			return;
		}
		catch
		{
			if (_client is not null)
				await HandleDisconnect(shouldReconnect);

			return;
		}
		finally
		{
			_connectionLock.Release();
		}
	}

	private async Task InitializeMessageChannel(CancellationToken cancelToken)
	{
		var messageService = _messageServiceFactory.Create(_client!, cancelToken);
		await messageService.BeginListening();
		_OnDisconnected = messageService.Dispose;
	}

	private async Task HandleDisconnect(bool shouldReconnect)
	{
		await _logger.Error("Disconnected from peer.");
		TearDown(_connectCancelTokenSource);
		// TODO: I don't recognize this pattern
		_connectCancelTokenSource = _rootToken is { } rootToken
			? CancellationTokenSource.CreateLinkedTokenSource(rootToken)
			: null;

		// NB: TcpClient.Close calls .Dispose.
		_client?.Close();
		_client = null;
		_OnDisconnected?.Invoke();
		_OnDisconnected = null;
		if(_lastKnownPeer is not null)
		{
			_repository.RemoveById(_lastKnownPeer.Id);
			_eventBus.OnPeerDisconnected(_lastKnownPeer.Id);	
		}
		if(shouldReconnect)
			StartReconnect();
	}

	private void StartReconnect()
	{
		// NB: guard against dangerous usage (called before initialization or after disposal)
		if (_disposed || _rootToken is null || _lastKnownPeer is null)
			return;

		if (_reconnectTask is { IsCompleted: false })
			return;

		TearDown(_reconnectSource);
		_reconnectSource = CancellationTokenSource.CreateLinkedTokenSource(_rootToken.Value, _shutdownSource.Token);
		_reconnectTask = TryReconnect(_reconnectSource.Token);
	}

	private async Task TryReconnect(CancellationToken cancelToken)
	{
		// NB: need to try-catch entering the lock separately - otherwise we risk entering
		// a finally block that would throw off the semaphore count.
		try
		{
			await _reconnectionLock.WaitAsync(cancelToken);
		}
		catch (OperationCanceledException)
		{
			return;
		}
		
		try
		{
			var peer = _lastKnownPeer;
			if (peer != null)
			{
				await _logger.Info("Attempting to reconnect...");
				var delay = TimeSpan.FromSeconds(1);
				var timeoutLimitInSeconds = 10;
				while (!cancelToken.IsCancellationRequested && delay.TotalSeconds < timeoutLimitInSeconds)
				{
					await MakeOutGoingConnection(peer.Endpoint, cancelToken, false);
					if (IsConnected) {
						_repository.AddPeer(peer);
						await _logger.Success("Reconnected to peer!");
						return;
					} 

					// TODO: why add this inner try-catch? Why not catch this exception in the outer?
					try {
						await Task.Delay(delay, cancelToken);
					}
					catch (OperationCanceledException) {
						return;
					}

					delay = TimeSpan.FromSeconds(delay.TotalSeconds * 1.5);
				}
				// NB: handle a retry timeout separately from a cancellation request.
				if (delay.TotalSeconds >= timeoutLimitInSeconds)
				{
					if (ReferenceEquals(_lastKnownPeer, peer))
						_lastKnownPeer = null;

					await _logger.Error("Unable to reconnect.");	
				}
			}
		}
		finally
		{
			_reconnectionLock.Release();
		}
	}

	// TODO: is this even doing anything?
	private void GuaranteeLinkedCancellationTokens(CancellationToken cancelToken)
	{
		_rootToken ??= cancelToken;
		_connectCancelTokenSource ??= CancellationTokenSource.CreateLinkedTokenSource(cancelToken);	
	}

	public static void TearDown(CancellationTokenSource? source)
	{
		if(source is null)
			return;

		source.Cancel();
		source.Dispose();
	}

	public void Dispose()
	{
		if (_disposed)
			return;

		_disposed = true;
		TearDown(_shutdownSource);
		TearDown(_connectCancelTokenSource);
		// TODO: why no TearDown here?
		_reconnectSource?.Cancel();
		_client?.Close();
		_client = null;
		_OnDisconnected?.Invoke();
		_OnDisconnected = null;	
	}
}