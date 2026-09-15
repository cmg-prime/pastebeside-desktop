using System.Net;
using System.Net.Sockets;
using System.Text;
using PasteBeside.ClientFriendlyLog;
using PasteBeside.Eventing;

namespace PasteBeside.PeerToPeer;

public class PeerDiscoveryService
{
	// NB: discovery port is any arbitrary free port.
	private const int _discoveryPort = 41234;
	// NB: make app ID part of the payload so listeners can filter out unintentionally received datagrams.
	private const string _appId = "pastebeside-p2p";
	private readonly string _localIdentifier = PeerIdentifier.New();

	private readonly ClientLogger _logger;
	private readonly UdpClient _discoveryListener;
	private readonly ConnectionRequestListener _connectionListener;
	private readonly PeerConnectionClient _peerClient;
	private readonly UdpClient _broadcaster;
	private readonly CancellationTokenSource _cancelTokenSource;
	private readonly PeerRepository _repository;
	private readonly EventBus _eventBus;

	private IPEndPoint? _connectionListenerEndpoint;

	public PeerDiscoveryService(ClientLogger logger, ConnectionRequestListener listener, PeerConnectionClient peerClient, PeerRepository repository, EventBus eventBus)
	{
		_logger = logger;
		_cancelTokenSource = new CancellationTokenSource();
		_discoveryListener = new UdpClient();
		// NB: allowing multiple listeners on the same socket out of an abundance of caution - no compelling reason not to,
		// and this eliminates dev concerns about fast restarts or multiple local instances attempting overlapping claims
		// before the socket is released.
		_discoveryListener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
		_discoveryListener.Client.Bind(new IPEndPoint(IPAddress.Any, _discoveryPort));
		_broadcaster = new UdpClient { EnableBroadcast = true };
		_connectionListener = listener;
		_peerClient = peerClient;
		_repository = repository;
		_eventBus = eventBus;
	}

	public async Task BeginDiscovery()
	{
		_connectionListenerEndpoint = await _connectionListener.InitializeListener(_cancelTokenSource.Token);
#pragma warning disable CS4014
		ListenForPeer(_cancelTokenSource.Token);
#pragma warning restore CS4014
		await Broadcast(_cancelTokenSource.Token);
	}

	private async Task ListenForPeer(CancellationToken cancelToken)
	{
		await _logger.Info("Starting peer discovery loop...");
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
				var peer = new Peer(peerIdentifier, peerEndpoint);
				_repository.AddPeer(peer);
				_eventBus.OnPeerDiscovered(peer);

				// NB: more robust to guard on GUID than address/port - determining your canonical address 
				// involves a surprising number of edge cases. (What if you have ethernet *and* WiFi active? Or
				// a VPN, or some other more or less niche network interface? What if your DHCP lease expires
				// while your machine sleeps?)
				if(peerIdentifier.Equals(_localIdentifier))
					continue;

				if (!_peerClient.IsConnected)
				{
					// TODO: the PeerConnectionClient, at the TCP level, doesn't know broadcast information
					// (i.e. peerId), so it won't be able to call EventBus.OnPeerDisconnected. That is the 
					// relevant connection, however (and where we should call .OnPeerConnected, too) - 
					// how do we fix?
					// NB: We can't just pass in peerId here. The ConnectionRequestListener (which calls
					// _peerClient.MakeIncomingConnection) is never going to have that information.
					await _peerClient.MakeOutgoingConnection(peerEndpoint, cancelToken);				}

				// NB: make sure new peers know about this client.
				await Broadcast(cancelToken);
				await _logger.Success("Peer discovered!");
			}
			catch (OperationCanceledException) { 
				await _logger.Info("Canceled peer discovery loop.");
				break; 
			}
			catch (Exception e) {
				await _logger.Error($"Problem during peer discovery: {e.Message}");
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
			var discoveryPayload = Encoding.UTF8.GetBytes($"{_appId}:{_localIdentifier}:{_connectionListenerEndpoint!.Port}");
			await _broadcaster.SendAsync(discoveryPayload, discoveryPayload.Length, broadcastEndpoint);
			_eventBus.OnLocalPeerConfigured(new Peer(_localIdentifier, _connectionListenerEndpoint));
			await _logger.Info("Discovery info broadcast.");
		}
		catch (Exception e) { 
			await _logger.Error($"Problem broadcasting discovery info: {e.Message}");
		}
	}

	public void Dispose()
	{
		_cancelTokenSource.Cancel();
		_discoveryListener.Dispose();
		_connectionListener.Dispose();
		_peerClient.Dispose();
		_broadcaster.Dispose();
	}
}