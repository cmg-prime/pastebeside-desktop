using System.Net;
using System.Net.Sockets;
using PasteBeside.ClientFriendlyLog;

namespace PasteBeside.PeerToPeer;

public class PeerListener
{
	private readonly ClientLogger _logger;
	private readonly PeerConnectionClient _peerClient;
	private readonly TcpListener _listener;
	private CancellationTokenSource? _cancelTokenSource;

	public PeerListener(ClientLogger logger, PeerConnectionClient peerClient)
	{
		_logger = logger;
		_peerClient = peerClient;
		_listener = new TcpListener(IPAddress.Any, 0);
	}

	// NB: can't meaningfully call this before _listener.Start: instantiating the listener with port 0 
	// means that the OS  dynamically assigns a port on .Start. For this special case, the post-.Start port
	// differs from the constructor argument.
	public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

	public async Task BeginListening(CancellationToken cancelToken)
	{
		_cancelTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancelToken);
		await _logger.Info("Listening for peer connection...");
		_listener.Start();
		while (true)
		{
			TcpClient incomingTcpClient;
			try
			{
				incomingTcpClient = await _listener.AcceptTcpClientAsync(_cancelTokenSource.Token);
				await _logger.Info("Incoming peer connection accepted!");
				// NB: if we were already connected, don't boot the current peer in favor of the new one.
				if (_peerClient.IsConnected) {
					incomingTcpClient.Close();
					return;
				}

				await _peerClient.MakeIncomingConnection(incomingTcpClient, _cancelTokenSource.Token);
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
		_cancelTokenSource?.Cancel();
		_listener.Dispose();
		_peerClient.Dispose();
	}
}