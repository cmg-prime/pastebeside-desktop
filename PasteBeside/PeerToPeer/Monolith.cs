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
	private readonly DataChannel _dataChannel;
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

	//TCP CONNECTION
	private readonly TcpListener _connectionListener;
	private Task? _tcpListenLoop;

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

		//TCP CONNECTION
		_connectionListener = new TcpListener(IPAddress.Any, 0);
	}

	public async Task BeginDiscovery()
	{
		// NB: this isn't strictly necessary - all methods have access to the _cancelTokenSource field -
		// but incautious access to the CTS can introduce race conditions with disposal, and passing down
		// a single, stable reference to the token obviates those concerns regardless of future changes.
		CancellationToken cancelToken;
		lock (_lifecycleLock)
		{
			if(_isDisposed || _cancelTokenSource!.IsCancellationRequested)
				throw new Exception("Discovery operations have already been canceled or disposed.");

			// NB: allowing multiple listeners on the same socket out of an abundance of caution - this 
			// eliminates dev concerns about fast restarts or multiple local instances attempting 
			// overlapping claims before the socket is released.
			_discoveryListener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
			// NB: because .Bind can throw (e.g. on permissions), configure the _discoveryListener here 
			// rather than in the constructor, so the newly constructed UdpClient can't ever leak.
			_discoveryListener.Client.Bind(new IPEndPoint(IPAddress.Any, _discoveryPort));			
			cancelToken = _cancelTokenSource!.Token;
			_logger.Info("Listening for incoming connection requests...");
			_connectionListener.Start();
			_tcpListenerEndpoint = (IPEndPoint)_connectionListener.LocalEndpoint;
			// NB: prevent race with disposal by ensuring the listen tasks are assigned within the lock.
			// Also: ListenForPeer represents a subtle async edge case. The code runs synchronously until 
			// the first *incomplete* task; if the task result is already queued - i.e. a datagram already
			// came in - we just keep hogging the lock. Task.Run sidesteps this issue by sending the task
			// to the thread pool regardless.
			_tcpListenLoop = Task.Run(() => ListenForConnectionRequest(cancelToken));
			_discoveryListenLoop = Task.Run(() => ListenForPeer(cancelToken));
		}
		var peer = new Peer(_identityService.Identifier, _tcpListenerEndpoint);
		_discoveredPeerRepo.AddPeer(peer);
		_eventBus.OnLocalPeerConfigured(new Peer(_identityService.Identifier, _tcpListenerEndpoint));
		_eventBus.OnPeerDiscovered(peer);

		await Broadcast(cancelToken);
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
			catch (ObjectDisposedException){  }
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
			// NB: ObjectDisposedException could be thrown from within dependencies - make sure we're
			// not greedily catching unrelated exceptions.
			catch (ObjectDisposedException) when (cancelToken.IsCancellationRequested){
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
		var (shutdownStarted, cancelTokenSource, _) = TryBeginShutdown();
		if(!shutdownStarted)
			return;

		cancelTokenSource!.Cancel();
		// NB: only close sockets after we've tried to resolve (cancel) all tasks relying on them.
		DisposeResources();
		cancelTokenSource!.Dispose();
	}

	public async ValueTask DisposeAsync()
	{
		var (shutdownStarted, cancelTokenSource, loopTasks) = TryBeginShutdown();
		if(!shutdownStarted)
			return;
		
		// NB: ensure that if any loop task throws an exception, we handle it.
		try
		{
			cancelTokenSource!.Cancel();
			await Task.WhenAll(loopTasks).ConfigureAwait(false);
		}
		catch { }
		finally {
			DisposeResources();
			cancelTokenSource!.Dispose();
		}
	}

	// TODO: good use case for an Either<T>. Are there others?
	private (bool shutdownStarted, CancellationTokenSource? cancelTokenSource, List<Task> loopTasks) TryBeginShutdown()
	{
		// NB: CTS could theoretically have cancellation callbacks registered. Don't want to hold the lock
		// to execute them if we don't have to. Ditto loop tasks.
		lock (_lifecycleLock)
		{
			if (_isDisposed) 
				return (false, null, []);

			_isDisposed = true;
			var cancelTokenSource = _cancelTokenSource;
			_cancelTokenSource = null;
			// NB: need this null guard because BeginDiscovery could throw (or never have been called).
			var loopTasks = new []{ _tcpListenLoop, _discoveryListenLoop }
				.Where(task => task is not null)
				.Cast<Task>()
				.ToList();

			_tcpListenLoop = _discoveryListenLoop = null;
			return (true, cancelTokenSource, loopTasks);
		}
	}

	private void DisposeResources()
	{
		_connectionListener.Dispose();
		_discoveryListener.Dispose();
		_broadcaster.Dispose();
	}
}