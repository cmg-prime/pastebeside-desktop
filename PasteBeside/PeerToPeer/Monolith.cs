using System.Net;
using System.Net.Sockets;
using System.Text;
using PasteBeside.ClientFriendlyLog;
using PasteBeside.Eventing;

namespace PasteBeside.PeerToPeer;

public class Monolith: IDisposable, IAsyncDisposable
{
	private readonly ClientLogger _logger;
	private readonly EventBus _eventBus;
	private readonly IdentityService _identityService;
	private readonly Lock _lifecycleLock;
	private CancellationTokenSource? _cancelTokenSource;
	private bool _isDisposed;

	//DISCOVERY
	// NB: discovery port is any arbitrary free port.
	private const int _discoveryPort = 41234;
	// NB: make app ID part of the payload so listeners can filter out unintentionally received datagrams.
	private const string _appId = "pastebeside-p2p";
	private readonly UdpClient _discoveryListener;
	private readonly UdpClient _broadcaster;
	private readonly DiscoveredPeerRepository _discoveredPeerRepo;

	private IPEndPoint? _tcpListenerEndpoint;
	private Task? _discoveryListenLoop;

	//LISTENER
	private readonly TcpListener _connectionListener;
	private Task? _tcpListenLoop;

	//DATACHANNEL
	private readonly DataChannel _dataChannel;

	public Monolith(ClientLogger logger, EventBus eventBus, IdentityService identityService, DiscoveredPeerRepository discoveredPeerRepo, DataChannel dataChannel)
	{
		_logger = logger;
		_eventBus = eventBus;
		_identityService = identityService;
		_dataChannel = dataChannel;
		_lifecycleLock = new();
		_isDisposed = false;

		//DISCOVERY
		_cancelTokenSource = new();
		_discoveryListener = new UdpClient();
		_broadcaster = new UdpClient { EnableBroadcast = true };
		_discoveredPeerRepo = discoveredPeerRepo;

		//TCP LISTENER
		_connectionListener = new TcpListener(IPAddress.Any, 0);
	}

	public async Task BeginDiscovery()
	{
		// NB: this isn't strictly necessary - all methods have access to the _cancelTokenSource field -
		// but direct access to the CTS can introduce race conditions with disposal, and passing down
		// a single, stable reference to a CancellationToken obviates those concerns regardless of future
		// code changes.
		CancellationToken cancelToken;
		lock (_lifecycleLock)
		{
			if(_isDisposed || _cancelTokenSource!.IsCancellationRequested)
				throw new Exception("Discovery operations have already been canceled or disposed.");

			// NB: allowing multiple listeners on the same socket out of an abundance of caution - no compelling reason not to,
			// and this eliminates dev concerns about fast restarts or multiple local instances attempting overlapping claims
			// before the socket is released.
			_discoveryListener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
			// NB: because .Bind can throw (e.g. on perimissions), configure the _discoveryListener here rather
			// than the constructor, so the newly constructor UdpClient doesn't ever leak.
			_discoveryListener.Client.Bind(new IPEndPoint(IPAddress.Any, _discoveryPort));			
			cancelToken = _cancelTokenSource!.Token;
		}

		_tcpListenerEndpoint = InitializeConnectionListener(cancelToken);
		var peer = new Peer(_identityService.Identifier, _tcpListenerEndpoint);
		_discoveredPeerRepo.AddPeer(peer);
		_eventBus.OnLocalPeerConfigured(new Peer(_identityService.Identifier, _tcpListenerEndpoint));
		_eventBus.OnPeerDiscovered(peer);

		_discoveryListenLoop = ListenForPeer(cancelToken);
		await Broadcast(cancelToken);
	}

	private IPEndPoint InitializeConnectionListener(CancellationToken cancelToken)
	{
		_logger.Info("Listening for incoming connection requests...");
		_connectionListener.Start();
		_tcpListenLoop = ListenForConnectionRequest(cancelToken);

		// NB: guarantee _listener.Start executes before callers can access the listener's port. (Since we
		// instantiate the listener with port 0, the OS dynamically assigns the "real" port on .Start.)
		return (IPEndPoint)_connectionListener.LocalEndpoint;
	}

	private async Task ListenForPeer(CancellationToken cancelToken)
	{
		_logger.Info("Starting peer discovery loop...");
		while (!cancelToken.IsCancellationRequested)
		{
			UdpReceiveResult datagram;
			try {
				datagram = await _discoveryListener.ReceiveAsync(cancelToken);
				var payloadText = Encoding.UTF8.GetString(datagram.Buffer);
				var discoveryInfo = payloadText.Split(':');
				if(discoveryInfo.Length != 3 || !discoveryInfo[0].Equals(_appId))
					continue;

				if(!int.TryParse(discoveryInfo[2], out var peerPort))
					continue; 

				var peerIdentifier = discoveryInfo[1];
				var peerEndpoint = new IPEndPoint(datagram.RemoteEndPoint.Address, peerPort);
				// NB: this may not catch loopback broadcasts, for which the source port is typically ephemeral.
				if(_discoveredPeerRepo.SearchById(peerIdentifier) is not null)
					continue;

				if(peerIdentifier.Equals(_identityService.Identifier))
					continue;

				var peer = new Peer(peerIdentifier, peerEndpoint);
				_discoveredPeerRepo.AddPeer(peer);
				_eventBus.OnPeerDiscovered(peer);
				// NB: make sure new peers know about this client (*before* we might try to connect to them).
				await Broadcast(cancelToken);
				await _dataChannel.MakeOutgoingConnection(peerEndpoint, cancelToken);

				_logger.Success("Peer discovered!");
			}
			catch (OperationCanceledException) { 
				_logger.Info("Canceled peer discovery loop.");
				break; 
			}
			catch (Exception e) {
				_logger.Error($"Problem during peer discovery: {e.Message}");
				continue;
			}
		}
	}

	private async Task Broadcast(CancellationToken cancelToken)
	{
		if(cancelToken.IsCancellationRequested)
			return;

		var broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, _discoveryPort);

		try {
			var discoveryPayload = Encoding.UTF8.GetBytes($"{_appId}:{_identityService.Identifier}:{_tcpListenerEndpoint!.Port}");
			await _broadcaster.SendAsync(discoveryPayload, discoveryPayload.Length, broadcastEndpoint);
			_logger.Info("Discovery info broadcast.");
		}
		catch (Exception e) { 
			_logger.Error($"Problem broadcasting discovery info: {e.Message}");
		}
	}

	private async Task ListenForConnectionRequest(CancellationToken cancelToken)
	{
		while (!cancelToken.IsCancellationRequested)
		{
			TcpClient? incomingTcpClient = null;
			try
			{
				incomingTcpClient = await _connectionListener.AcceptTcpClientAsync(cancelToken);
				// NB: if we were already connected, don't boot the current peer in favor of the new one.
				if (_dataChannel.IsConnected) {
					incomingTcpClient.Close();
					continue;
				}

				await _dataChannel.MakeIncomingConnection(incomingTcpClient, cancelToken);
				_logger.Info("Incoming peer connection accepted!");
			}
			catch (OperationCanceledException) { 
				incomingTcpClient?.Close();
				break; 
			}
			catch (Exception e) {
				incomingTcpClient?.Close();
				_logger.Error($"Problem listening for peer connection: {e.Message}");
				// NB: we don't stop listening on network error; not our problem.
				continue;
			}
		}
	}

	public void Dispose()
	{
		// NB: CTS could theoretically have cancellation callbacks registered. Don't want to hold the lock
		// to execute them if we don't have to. Ditto loop tasks.
		CancellationTokenSource? CancelTokenSource;
		lock (_lifecycleLock)
		{
			// NB: guard against concurrent disposal.
			if(_isDisposed)
				return;

			_isDisposed = true;
			CancelTokenSource = _cancelTokenSource;
			_cancelTokenSource = null;
		}
		CancelTokenSource?.Cancel();
		CancelTokenSource?.Dispose();
		// NB: only close sockets after we've tried to resolve (cancel) all tasks relying on them.
		_connectionListener.Dispose();
		_discoveryListener.Dispose();
		_broadcaster.Dispose();
		_dataChannel.Dispose();
	}

	public async ValueTask DisposeAsync()
	{
		// NB: CTS could theoretically have cancellation callbacks registered. Don't want to hold the lock
		// to execute them if we don't have to. Ditto loop tasks.
		CancellationTokenSource? CancelTokenSource;
		List<Task> loopTasks = [];
		lock (_lifecycleLock)
		{
			// NB: guard against concurrent disposal.
			if(_isDisposed)
				return;

			_isDisposed = true;
			CancelTokenSource = _cancelTokenSource;
			if(_tcpListenLoop is not null)
			{
				loopTasks.Add(_tcpListenLoop);
				_tcpListenLoop = null;
			}
			if(_discoveryListenLoop is not null)
			{
				loopTasks.Add(_discoveryListenLoop);
				_discoveryListenLoop = null;
			}
		}
		CancelTokenSource?.Cancel();
		await Task.WhenAll(loopTasks);
		// NB: the loop tasks may still rely on reading the state of the CTS, so defer disposal until
		// they've resolved.
		_cancelTokenSource = null;
		CancelTokenSource?.Dispose();
		// NB: only close sockets after we're sure all tasks relying on them have resolved.
		_connectionListener.Dispose();
		_discoveryListener.Dispose();
		_broadcaster.Dispose();
	}
}