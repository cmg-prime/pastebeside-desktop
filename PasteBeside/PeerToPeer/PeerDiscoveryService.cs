using System.Net;
using System.Net.Sockets;
using System.Text;
using PasteBeside.ClientFriendlyLog;

namespace PasteBeside.PeerToPeer;

public class PeerDiscoveryService
{
	// NB: discovery port is any arbitrary free port.
	private const int _discoveryPort = 41234;
	// NB: make app ID part of the payload so listeners can filter out unintentionally received datagrams.
	private const string _appId = "pastebeside-p2p";
	private readonly Guid _instanceId = Guid.NewGuid();

	private readonly ClientLogger _logger;
	private readonly UdpClient _discoveryListener;
	private readonly ConnectionRequestListener _connectionListener;
	private readonly PeerConnectionClient _peerClient;
	private readonly UdpClient _broadcaster;
	private readonly CancellationTokenSource _cancelTokenSource;

	private int? _connectionListenerPort;

	public PeerDiscoveryService(ClientLogger logger, ConnectionRequestListener listener, PeerConnectionClient peerClient)
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
	}

	public async Task BeginDiscovery()
	{
		_connectionListenerPort = (await _connectionListener.InitializeListener(_cancelTokenSource.Token)).Port;
		await Broadcast(_cancelTokenSource.Token);
#pragma warning disable CS4014
		ListenForPeer(_cancelTokenSource.Token);
#pragma warning restore CS4014
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

				// NB: more robust to guard on GUID than address/port - determining your canonical address 
				// involves a surprising number of edge cases. (What if you have ethernet *and* WiFi active? Or
				// a VPN, or some other more or less niche network interface? What if your DHCP lease expires
				// while your machine sleeps?)
				if(!Guid.TryParse(discoveryInfo[1], out var peerInstanceId) || peerInstanceId == _instanceId)
					continue; 

				if(!int.TryParse(discoveryInfo[2], out var peerPort))
					continue; 

				var peerEndpoint = new IPEndPoint(datagram.RemoteEndPoint.Address, peerPort);
				await _peerClient.MakeOutgoingConnection(peerEndpoint, cancelToken);

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
			var discoveryPayload = Encoding.UTF8.GetBytes($"{_appId}:{_instanceId}:{_connectionListenerPort}");
			await _broadcaster.SendAsync(discoveryPayload, discoveryPayload.Length, broadcastEndpoint);
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