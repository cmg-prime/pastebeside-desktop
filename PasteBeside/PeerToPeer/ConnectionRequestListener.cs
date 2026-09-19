using System.Net;
using System.Net.Sockets;
using PasteBeside.ClientFriendlyLog;

namespace PasteBeside.PeerToPeer;

public class ConnectionRequestListener
{
	private readonly ClientLogger _logger;
	private readonly ConnectionBroker _connectionBroker;
	private readonly TcpListener _listener;

	private CancellationTokenSource? _cancelTokenSource;

	public ConnectionRequestListener(ClientLogger logger, ConnectionBroker connectionBroker)
	{
		_logger = logger;
		_connectionBroker = connectionBroker;
		_listener = new TcpListener(IPAddress.Any, 0);
	}

	// NB: guarantee _listener.Start executes before callers can access the listener's port. Since we
	// instantiate the listener with port 0, the OS dynamically assigns the "real" port on .Start.
	public async Task<IPEndPoint> InitializeListener(CancellationToken cancelToken)
	{
		_cancelTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancelToken);
		await _logger.Info("Listening for incoming connection requests...");
		_listener.Start();
#pragma warning disable CS4014
		ExecuteListenLoop();
#pragma warning restore CS4014

		return (IPEndPoint)_listener.LocalEndpoint;
	}

	public async Task ExecuteListenLoop()
	{
		while (true)
		{
			TcpClient? incomingTcpClient = null;
			try
			{
				incomingTcpClient = await _listener.AcceptTcpClientAsync(_cancelTokenSource!.Token);
				// NB: if we were already connected, don't boot the current peer in favor of the new one.
				// TODO: should the connection broker even know this?
				if (_connectionBroker.IsConnected) {
					incomingTcpClient.Close();
					return;
				}

				await _connectionBroker.MakeIncomingConnection(incomingTcpClient, _cancelTokenSource.Token);
				await _logger.Info("Incoming peer connection accepted!");
			}
			catch (OperationCanceledException) { 
				TearDownTcpClient(incomingTcpClient);
				break; 
			}
			catch (Exception e) {
				TearDownTcpClient(incomingTcpClient);
				await _logger.Error($"Problem listening for peer connection: {e.Message}");
				// NB: we don't stop listening on network error; not our problem.
				continue;
			}
		}

		static void TearDownTcpClient(TcpClient? tcpClient)
		{
			tcpClient?.Close();
			tcpClient = null;
		}
	}

	public void Dispose()
	{
		_cancelTokenSource?.Cancel();
		_listener.Dispose();
	}
}