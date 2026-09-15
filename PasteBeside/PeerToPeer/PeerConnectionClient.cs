using System.Net;
using System.Net.Sockets;
using PasteBeside.ClientFriendlyLog;
using PasteBeside.Eventing;

namespace PasteBeside.PeerToPeer;

public class PeerConnectionClient
{
	private readonly ClientLogger _logger;
	private readonly SemaphoreSlim _connectionLock;
	private readonly MessageServiceFactory _messageServiceFactory;
	private readonly PeerRepository _repository;
	private readonly EventBus _eventBus;

	private Action? _OnDisconnected;
	private TcpClient? _client;
	private Peer? _lastKnownPeer;
	private CancellationTokenSource? _connectCancelTokenSource;
	private CancellationTokenSource? _reconnectCancelTokenSource;

	public PeerConnectionClient(ClientLogger logger, MessageServiceFactory messageServiceFactory, PeerRepository repository, EventBus eventBus)
	{
		_logger = logger;
		_connectionLock = new SemaphoreSlim(1, 1);
		_messageServiceFactory = messageServiceFactory;
		_repository = repository;
		_eventBus = eventBus;
	}

	public bool IsConnected => _client?.Connected == true;

	public async Task MakeIncomingConnection(TcpClient incomingClient, CancellationToken cancelToken)
	{
		GuaranteeLinkedCancellationTokens(cancelToken);
		await HandleConnectCommand(
			async () =>
			{
				// NB: if we were trying to reconnect before, we definitely aren't now.
				_reconnectCancelTokenSource = _reconnectCancelTokenSource!.Recycle();
				_client = incomingClient;
				_lastKnownPeer = _repository.FindByEndpoint((IPEndPoint)incomingClient.Client.RemoteEndPoint!);

				_eventBus.OnPeerConnected(_lastKnownPeer);
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

	private async Task MakeOutGoingConnection(IPEndPoint endpoint, CancellationToken cancelToken)
	{
		await HandleConnectCommand(
			async () =>
			{
				_lastKnownPeer = _repository.FindByEndpoint(endpoint!);
				_client = new TcpClient();
				await _client.ConnectAsync(endpoint.Address, endpoint.Port, cancelToken);
				await _logger.Info("Outgoing peer connection accepted!");
				_eventBus.OnPeerConnected(_lastKnownPeer);

				await InitializeMessageChannel(cancelToken);
			},
			cancelToken
		);
	}

	private async Task HandleConnectCommand(Func<Task> Connect, CancellationToken cancelToken)
	{
		await _connectionLock.WaitAsync(cancelToken);
		try
		{
			if (IsConnected) 
				return;

			await Connect();
		}
		catch
		{
			await HandleDisconnect();
			return;
		}
		finally
		{
			_connectionLock.Release();
		}
	}

	private void GuaranteeLinkedCancellationTokens(CancellationToken cancelToken)
	{
		_connectCancelTokenSource ??= CancellationTokenSource.CreateLinkedTokenSource(cancelToken);	
		_reconnectCancelTokenSource ??= CancellationTokenSource.CreateLinkedTokenSource(cancelToken);
	}

	private async Task InitializeMessageChannel(CancellationToken cancelToken)
	{
		var messageService = _messageServiceFactory.Create(
			_client!, 
			cancelToken
		);
		await messageService.BeginListening();
		_OnDisconnected = messageService.Dispose;
	}

	private async Task HandleDisconnect()
	{
		await _logger.Error("Disconnected from peer.");
		_connectCancelTokenSource = _connectCancelTokenSource!.Recycle();
		// NB: TcpClient.Close calls .Dispose.
		_client?.Close();
		_client = null;
		_OnDisconnected?.Invoke();
		_repository.RemoveById(_lastKnownPeer!.Id);
		_eventBus.OnPeerDisconnected(_lastKnownPeer!.Id);
#pragma warning disable CS4014
		TryReconnect();
#pragma warning restore CS4014
	}

	private async Task TryReconnect()
	{
		if (_lastKnownPeer != null)
		{
			await _logger.Info("Attempting to reconnect...");
			
			var cancelToken = _reconnectCancelTokenSource!.Token;
			var delay = TimeSpan.FromSeconds(1);
			while (!cancelToken.IsCancellationRequested && delay.TotalSeconds < 10)
			{
				await MakeOutGoingConnection(_lastKnownPeer!.Endpoint, cancelToken);
				if (IsConnected) {
					_repository.AddPeer(_lastKnownPeer);
					await _logger.Success("Reconnected to peer!");
					return;
				} 

				try {
					await Task.Delay(delay, cancelToken);
				}
				catch (OperationCanceledException) {
					_reconnectCancelTokenSource = _reconnectCancelTokenSource.Recycle();			
					break;
				}

				delay = TimeSpan.FromSeconds(delay.TotalSeconds * 1.5);
			}
		}
		await _logger.Error("Unable to reconnect.");
	}

	public void Dispose()
	{
		_reconnectCancelTokenSource?.TearDown();
		_connectCancelTokenSource?.TearDown();
		_client?.Close();
		_OnDisconnected?.Invoke();
	}

}