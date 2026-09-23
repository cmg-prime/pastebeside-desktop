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
	private readonly SemaphoreSlim _stateLock;
	private Task? _listenTask;
	private bool _isDisposed;

	public ConnectionRequestListener(ClientLogger logger, DataChannel dataChannel)
	{
		_logger = logger;
		_dataChannel = dataChannel;
		_listener = new TcpListener(IPAddress.Any, 0);
		_stateLock = new(1,1);
		_disposeLock = new();
		_isDisposed = false;
	}

	// NB: guarantee _listener.Start executes before callers can access the listener's port. Since we
	// instantiate the listener with port 0, the OS dynamically assigns the "real" port on .Start.
	public async Task<IPEndPoint> InitializeListener(CancellationToken cancelToken)
	{
		// NB: if for some reason we're re-initializing the listener, make sure to clean up after the 
		// potentially extant operation.
		CancellationToken loopCancelToken;
		// NB: ConfigureAwait everywhere to minimize the risk of deadlock from Dispose waiting on 
		// a thread that's blocked for it.
		await _stateLock.WaitAsync(cancelToken).ConfigureAwait(false);
		try
		{
			ObjectDisposedException.ThrowIf(_isDisposed, this);
			// NB: if for some reason we're re-initializing the listener, make sure to clean up after the 
			// potentially extant operation.
			if(_cancelTokenSource is not null)
			{
				_cancelTokenSource.Cancel();
				_cancelTokenSource.Dispose();
				_cancelTokenSource = null;
			}
			if (_listenTask is not null)
			{
				try
				{
					await _listenTask.ConfigureAwait(false);
				}
				catch (OperationCanceledException) { /* NB: this is the expected outcome of re-initializing. */ }
				_listenTask = null;
			}

			_cancelTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancelToken);
			loopCancelToken = _cancelTokenSource.Token;
			_listener.Stop();
			_listener.Start();

			// TODO: run in background?
			_listenTask = ExecuteListenLoop(loopCancelToken);
			await _logger.Info("Listening for incoming connection requests...").ConfigureAwait(false);

			return (IPEndPoint)_listener.LocalEndpoint;
		}
		finally
		{
			_stateLock.Release();
		}
	}

	public async Task ExecuteListenLoop(CancellationToken cancelToken)
	{
		while (true && !cancelToken.IsCancellationRequested)
		{
			TcpClient? incomingTcpClient = null;
			try
			{
				incomingTcpClient = await _listener.AcceptTcpClientAsync(cancelToken).ConfigureAwait(false);
				var madeConnection = await _dataChannel.MakeIncomingConnection(incomingTcpClient, cancelToken).ConfigureAwait(false);
				if (madeConnection)
				{
					await _logger.Info("Incoming peer connection accepted!").ConfigureAwait(false);					
				}
				else
				{
					TearDownTcpClient(incomingTcpClient);
					await _logger.Error("Incoming connection could not be established.").ConfigureAwait(false);
				}
			}
			catch (OperationCanceledException) { 
				TearDownTcpClient(incomingTcpClient);
				break; 
			}
			catch (Exception e) {
				TearDownTcpClient(incomingTcpClient);
				await _logger.Error($"Problem listening for peer connection: {e.Message}").ConfigureAwait(false);
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

	public void Dispose() => DisposeAsync().AsTask().ConfigureAwait(false).GetAwaiter().GetResult();

	public async ValueTask DisposeAsync()
	{
		Task? listenTaskToAwait;
		await _stateLock.WaitAsync().ConfigureAwait(false);
		try
		{
			if (_isDisposed)
				return;

			_isDisposed = true;

			_cancelTokenSource?.Cancel();
			_cancelTokenSource?.Dispose();
			_cancelTokenSource = null;
			_listener.Dispose();

			listenTaskToAwait = _listenTask;
			_listenTask = null;
		}
		finally
		{
			_stateLock.Release();
		}

		// NB: safe to wait outside the lock. A concurrent call to InitializeListener will read _isDisposed
		// as soon as it acquires the lock, and throw the exception.
		if (listenTaskToAwait is not null)
		{
			try
			{
				await listenTaskToAwait.ConfigureAwait(false);
			}
			catch { /* NB: exceptions handled inside the listen loop. No-op. */ }
		}

		_stateLock.Dispose();
	}
}