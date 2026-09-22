using System.Net;
using System.Net.Sockets;
using PasteBeside.ClientFriendlyLog;

namespace PasteBeside.PeerToPeer;

public class ConnectionRequestListener: IDisposable
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
		CancellationToken loopCancelToken;
		lock (_disposeLock)
		{
			if(_cancelTokenSource is not null)
			{
				_cancelTokenSource.Cancel();
				_cancelTokenSource.Dispose();
			}	
			_cancelTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancelToken);
			loopCancelToken = _cancelTokenSource.Token;
			_listener.Stop();
			_listener.Start();
		}

		await _logger.Info("Listening for incoming connection requests...");
		// TODO: run in background?
#pragma warning disable CS4014
		ExecuteListenLoop(loopCancelToken);
#pragma warning restore CS4014

		return (IPEndPoint)_listener.LocalEndpoint;
	}

	public async Task ExecuteListenLoop(CancellationToken cancelToken)
	{
		while (true && !cancelToken.IsCancellationRequested)
		{
			TcpClient? incomingTcpClient = null;
			try
			{
				incomingTcpClient = await _listener.AcceptTcpClientAsync(cancelToken);
				var madeConnection = await _dataChannel.MakeIncomingConnection(incomingTcpClient, cancelToken);
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
		lock (_disposeLock)
		{
			// NB: I'd prefer to cancel outside the lock in case of long-running cancel callbacks - but
			// that's an unlikely design-time edge case, and it's more important to cancel the listen operation
			// before disposing the listener out from under it.
			_cancelTokenSource?.Cancel();
			_cancelTokenSource?.Dispose();
			_cancelTokenSource = null;
			_listener.Dispose();
		}
	}
}