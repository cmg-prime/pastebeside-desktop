using System.Net;
using System.Net.Sockets;
using PasteBeside.ClientFriendlyLog;

namespace PasteBeside.PeerToPeer;

public class ConnectionRequestListener
{
	private readonly ClientLogger _logger;
	private readonly DataChannel _dataChannel;
	private readonly TcpListener _listener;
	private readonly Lock _disposeLock;

	private CancellationTokenSource? _cancelTokenSource;

	public ConnectionRequestListener(ClientLogger logger, DataChannel dataChannel)
	{
		_logger = logger;
		_dataChannel = dataChannel;
		_listener = new TcpListener(IPAddress.Any, 0);
		_disposeLock = new();
	}

	// NB: guarantee _listener.Start executes before callers can access the listener's port. Since we
	// instantiate the listener with port 0, the OS dynamically assigns the "real" port on .Start.
	public async Task<IPEndPoint> InitializeListener(CancellationToken cancelToken)
	{
		// NB: if for some reason we're re-initializing the listener, make sure to clean up after the 
		// potentially extant operation.
		lock (_disposeLock)
		{
			if(_cancelTokenSource is not null)
			{
				_cancelTokenSource.Cancel();
				_cancelTokenSource.Dispose();
			}	
		}

		_cancelTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancelToken);
		await _logger.Info("Listening for incoming connection requests...");
		_listener.Start();
#pragma warning disable CS4014
		// TODO: run in background?
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
				if (_dataChannel.IsConnected) {
					incomingTcpClient.Close();
					return;
				}

				var madeConnection = await _dataChannel.MakeIncomingConnection(incomingTcpClient, _cancelTokenSource.Token);
				if (madeConnection)
				{
					await _logger.Info("Incoming peer connection accepted!");					
				}
				else
				{
					TearDownTcpClient(incomingTcpClient);
					await _logger.Error("Incoming connection could not be established.");
				}
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
		CancellationTokenSource? cancelTokenSource;
		lock (_disposeLock)
		{
			cancelTokenSource = _cancelTokenSource;
			_cancelTokenSource = null;
			_listener.Dispose();			
		}
		cancelTokenSource?.Cancel();
		cancelTokenSource?.Dispose();
	}
}