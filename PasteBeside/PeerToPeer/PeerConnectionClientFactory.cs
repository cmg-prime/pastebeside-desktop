using System.Net;
using System.Net.Sockets;
using PasteBeside.ClientFriendlyLog;

namespace PasteBeside.PeerToPeer;

public class PeerConnectionClientFactory(ClientLogger _logger)
{
	public PeerConnectionClient Create(Func<TcpClient, Action> OnConnected, CancellationToken rootCancelToken)
		=> new TcpPeerClient(_logger, OnConnected, rootCancelToken);

	public class TcpPeerClient: PeerConnectionClient
	{
		private readonly ClientLogger _logger;
		private readonly SemaphoreSlim _connectionLock;
		private readonly Func<TcpClient, Action> _OnConnected;
		
		private Action? _OnDisconnected;
		private TcpClient? _client;
		private IPEndPoint? _lastKnownPeer;
		private CancellationTokenSource _connectCancelTokenSource;
		private CancellationTokenSource _reconnectCancelTokenSource;

		public TcpPeerClient(ClientLogger logger, Func<TcpClient, Action> OnConnected, CancellationToken rootCancelToken)
		{
			_logger = logger;
			_connectionLock = new SemaphoreSlim(1, 1);
			_OnConnected = OnConnected;
			_connectCancelTokenSource = CancellationTokenSource.CreateLinkedTokenSource(rootCancelToken);
			_reconnectCancelTokenSource = CancellationTokenSource.CreateLinkedTokenSource(rootCancelToken);
		}

		public bool IsConnected => _client?.Connected == true;

		public async Task MakeIncomingConnection(TcpClient incomingClient)
		{
			await HandleConnectCommand(
				async () =>
				{
					// NB: if we were trying to reconnect before, we definitely aren't now.
					_reconnectCancelTokenSource = _reconnectCancelTokenSource.Recycle();
					_client = incomingClient;
					_lastKnownPeer = (IPEndPoint)_client.Client.RemoteEndPoint!;
					_OnDisconnected = _OnConnected(_client);		
				}, 
				_connectCancelTokenSource.Token
			);
		}

		public async Task MakeOutgoingConnection(IPEndPoint endpoint)
			=> await MakeOutGoingConnection(endpoint, _connectCancelTokenSource.Token);

		public async Task TryReconnect()
		{
			if (_lastKnownPeer != null)
			{
				await _logger.Info("Attempting to reconnect...");
				
				var cancelToken = _reconnectCancelTokenSource.Token;
				var delay = TimeSpan.FromSeconds(1);
				while (!cancelToken.IsCancellationRequested && delay.TotalSeconds < 10)
				{
					await MakeOutGoingConnection(_lastKnownPeer!, cancelToken);
					if (IsConnected) {
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

		private async Task MakeOutGoingConnection(IPEndPoint endpoint, CancellationToken cancelToken)
		{
			await HandleConnectCommand(
				async () =>
				{
					_lastKnownPeer = endpoint;
					_client = new TcpClient();
					await _client.ConnectAsync(endpoint.Address, endpoint.Port, cancelToken);
					await _logger.Info("Outgoing peer connection accepted!");

					_OnDisconnected = _OnConnected(_client);
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

		private async Task HandleDisconnect()
		{
			await _logger.Error("Disconnected from peer.");
			_connectCancelTokenSource = _connectCancelTokenSource.Recycle();
			// NB: TcpClient.Close calls .Dispose.
			_client?.Close();
			_client = null;
			_OnDisconnected?.Invoke();
#pragma warning disable CS4014
			TryReconnect();
#pragma warning restore CS4014
		}

		public void Dispose()
		{
			_reconnectCancelTokenSource.TearDown();
			_connectCancelTokenSource.TearDown();
			_client?.Close();
			_OnDisconnected?.Invoke();
		}

	}
}