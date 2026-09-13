using System.Net;
using System.Net.Sockets;
using PasteBeside.ClientFriendlyLog;

namespace PasteBeside.PeerToPeer;

public class PeerListenerFactory(ClientLogger _logger)
{
	public PeerListener Create(Action<TcpClient> OnConnected, CancellationToken rootCancelToken)
		=> new TcpPeerListener(_logger, OnConnected, rootCancelToken);

	private class TcpPeerListener : PeerListener
	{
		private readonly ClientLogger _logger;
		private readonly TcpListener _listener;
		private readonly CancellationTokenSource _cancelTokenSource;
		private readonly Action<TcpClient> _OnConnected;

		public TcpPeerListener(ClientLogger logger, Action<TcpClient> OnConnected, CancellationToken rootCancelToken)
		{
			_logger = logger;
			_listener = new TcpListener(IPAddress.Any, 0);
			_cancelTokenSource = CancellationTokenSource.CreateLinkedTokenSource(rootCancelToken);
			_OnConnected = OnConnected;
#pragma warning disable CS4014
			BeginListening();
#pragma warning restore CS4014
		}

		// NB: can't meaningfully call this before _listener.Start: instantiating the listener with port 0 
		// means that the OS  dynamically assigns a port on .Start. For this special case, the post-.Start port
		// differs from the constructor argument.
		public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

		private async Task BeginListening()
		{
			await _logger.Info("Listening for peer connection...");
			_listener.Start();
			while (true)
			{
				TcpClient connectedClient;
				try
				{
					connectedClient = await _listener.AcceptTcpClientAsync(_cancelTokenSource.Token);
					await _logger.Info("Incoming peer connection accepted!");
					_OnConnected(connectedClient);
				}
				catch (OperationCanceledException) { 
					break; 
				}
				catch (Exception e) {
					await _logger.Error($"Problem listening for peer connection: {e.Message}");
					// NB: we don't stop listening on network error; not our problem.
					continue;
				}
			}
		}

		public void Dispose()
		{
			_cancelTokenSource.Cancel();
			_listener.Dispose();
		}
	}
	
}