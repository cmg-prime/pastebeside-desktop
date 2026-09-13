using System.Net;
using System.Net.Sockets;
using System.Text;
using PasteBeside.ClientFriendlyLog;

namespace PasteBeside.PeerToPeer;

public class DiscoveryServiceFactory(ClientLogger _logger)
{
	public DiscoveryService Create(int incomingConnectionPort, Action<IPEndPoint> OnPeerDiscovered, CancellationToken cancelToken)
		=> new UdpDiscoveryService(_logger, incomingConnectionPort, OnPeerDiscovered, cancelToken);

	private class UdpDiscoveryService : DiscoveryService
	{
		// NB: discovery port is any arbitrary free port.
		private const int _discoveryPort = 41234;
		// NB: make app ID part of the payload so listeners can filter out unintentionally received datagrams.
		private const string _appId = "pastebeside-p2p";

		private readonly ClientLogger _logger;
		private readonly UdpClient _listener;
		private readonly UdpClient _broadcaster;
		private readonly byte[] _discoveryPayload;
		private readonly CancellationTokenSource _cancelTokenSource;

		private Action<IPEndPoint>? _OnPeerDiscovered;

		public UdpDiscoveryService(ClientLogger logger, int incomingConnectionPort, Action<IPEndPoint> OnPeerDiscovered, CancellationToken cancelToken)
		{
			_logger = logger;
			_cancelTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancelToken);
			_discoveryPayload = Encoding.UTF8.GetBytes($"{_appId}:{incomingConnectionPort}");
			_listener = new UdpClient();
			// NB: allowing multiple listeners on the same socket out of an abundance of caution - no compelling reason not to,
			// and this eliminates dev concerns about fast restarts or multiple local instances attempting overlapping claims
			// before the socket is released.
			_listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
			_listener.Client.Bind(new IPEndPoint(IPAddress.Any, _discoveryPort));
			_broadcaster = new UdpClient { EnableBroadcast = true };

			_OnPeerDiscovered = OnPeerDiscovered;
		}

		public async Task BeginDiscovery()
		{
			await Broadcast(_cancelTokenSource.Token);
	#pragma warning disable CS4014
			ListenUntilCanceled(_cancelTokenSource.Token);
	#pragma warning restore CS4014
		}

		private async Task ListenUntilCanceled(CancellationToken cancelToken)
		{
			await _logger.Info("Starting peer discovery loop...");
			while (!cancelToken.IsCancellationRequested)
			{
				UdpReceiveResult result;
				try {
					result = await _listener.ReceiveAsync(cancelToken);
					if(DiscoveredOwnBroadcast(result.Buffer))
						continue;
						
					await _logger.Success("Peer discovered!");
				}
				catch (OperationCanceledException) { 
					await _logger.Info("Canceled peer discovery loop.");
					break; 
				}
				catch (Exception e) {
					await _logger.Error($"Problem listening for peer discovery: {e.Message}");
					continue;
				}

				var text = Encoding.UTF8.GetString(result.Buffer);
				var parts = text.Split(':');
				if (parts.Length == 2 && parts[0] == _appId && int.TryParse(parts[1], out var peerPort))
				{
					var peerEndpoint = new IPEndPoint(result.RemoteEndPoint.Address, peerPort);
					_OnPeerDiscovered!(peerEndpoint);

					// NB: make sure new peers know about this client.
					await Broadcast(cancelToken);
				}
			}
		}

		private async Task Broadcast(CancellationToken cancelToken)
		{
			if(cancelToken.IsCancellationRequested)
				return;

			var broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, _discoveryPort);
			try {
				await _broadcaster.SendAsync(_discoveryPayload, _discoveryPayload.Length, broadcastEndpoint);
				await _logger.Info("Discovery info broadcast.");
			}
			catch (Exception e) { 
				await _logger.Error($"Problem broadcasting discovery info: {e.Message}");
			}
		}

		private bool DiscoveredOwnBroadcast(byte[] incomingPayload)
		{
			if (_discoveryPayload.Length != incomingPayload.Length)
				return false;

			for(var i = 0; i < _discoveryPayload.Length; i++)
				if(_discoveryPayload[i] != incomingPayload[i])
					return false;
			
			return true;
		}

		public void Dispose()
		{
			_cancelTokenSource.Cancel();
			_listener.Dispose();
			_broadcaster.Dispose();
		}
	}
}