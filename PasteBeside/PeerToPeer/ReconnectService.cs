using System.Net.Sockets;
using PasteBeside.ClientFriendlyLog;

namespace PasteBeside.PeerToPeer;

public class ReconnectService: IDisposable
{
	private readonly ClientLogger _logger;
	private readonly SemaphoreSlim _reconnectionLock;
	private readonly LastConnectedPeerRepository _lastConnectedPeerRepo;
	private readonly DiscoveredPeerRepository _discoveryRepo;
	private readonly OutgoingConnectionCommand _outgoingConnectionCommand;
	private readonly Lock _disposalLock;

	private bool _hasBeenDisposed;
	private CancellationTokenSource? _cancelTokenSource;

	public ReconnectService(ClientLogger logger, LastConnectedPeerRepository lastConnectedPeerRepo, DiscoveredPeerRepository discoveryRepo, OutgoingConnectionCommand makeOutgoingConnection)
	{
		_logger = logger;
		_reconnectionLock = new(1,1);
		_lastConnectedPeerRepo = lastConnectedPeerRepo;
		_discoveryRepo = discoveryRepo;
		_outgoingConnectionCommand = makeOutgoingConnection;
		_disposalLock = new();
		_hasBeenDisposed = false;
	}

	public async Task<TcpClient?> Reconnect(CancellationToken[] tokens)
	{
		if(tokens.Length == 0)
			return null;

		await _reconnectionLock.WaitAsync();

		// NB: this approach has three motivations.
		// 1. Keep a stable (i.e. local) reference to the token source at lock time - even if 
		//	  another call reassigns the _cancelTokenSource class field before we dispose the token source.
		// 2. Prevent race conditions with Dispose as we read and manage resources.
		// 3. Safely nullify the _cancelTokenSource class field - which we dispose through the
		//    local reference - so that it's not a dangling reference that might throw ObjectDisposedException
		//    when Dispose tries to dispose it again.
		CancellationTokenSource? cancelTokenSource = null;
		try
		{
			lock (_disposalLock)
			{
				if(_hasBeenDisposed || _lastConnectedPeerRepo.Get() is null)
					return null;

				cancelTokenSource = CancellationTokenSource.CreateLinkedTokenSource(tokens);
				_cancelTokenSource = cancelTokenSource;
			}	
			return await TryReconnect(cancelTokenSource.Token);
		}
		finally
		{
			lock (_disposalLock)
			{
				// NB: if another operation has set a new _cancelTokenSource, that operation now owns its lifecycle.
				if(ReferenceEquals(_cancelTokenSource, cancelTokenSource))
					_cancelTokenSource = null;
			}

			cancelTokenSource?.Dispose();
			_reconnectionLock.Release();
		}
	}

	private async Task<TcpClient?> TryReconnect(CancellationToken cancelToken)
	{
		var peer = _lastConnectedPeerRepo.Get();
		if(peer is null)
			return null;

		var delay = TimeSpan.FromSeconds(1);
		var timeoutLimitInSeconds = 10;
		try
		{
			while (!cancelToken.IsCancellationRequested && delay.TotalSeconds < timeoutLimitInSeconds)
			{
				await _logger.Info("Attempting to reconnect...");
				var client = await _outgoingConnectionCommand.Execute(peer.Endpoint, cancelToken);
				if (client?.Connected ?? false) {
					_discoveryRepo.AddPeer(peer);
					await _logger.Success("Reconnected to peer!");
					return client;
				}

				await Task.Delay(delay, cancelToken);
				delay = TimeSpan.FromSeconds(delay.TotalSeconds * 1.5);
			}
			// NB: handle a retry timeout separately from a cancellation request.
			if (delay.TotalSeconds >= timeoutLimitInSeconds)
			{
				if (ReferenceEquals(_lastConnectedPeerRepo.Get(), peer))
					_lastConnectedPeerRepo.Reset();

				await _logger.Error("Unable to reconnect.");	
			}	
		}
		catch (OperationCanceledException){ /* NB: all we'd do here is return null. No-op. */ }
		return null;
	}

	public void Dispose()
	{
		// NB: We want to Dispose the CancellationTokenSource outside the lock, where it can't block
		// or deadlock on potentially registered cancellation callbacks.
		CancellationTokenSource? stableReference;
		lock (_disposalLock)
		{
			// NB: guarantees idempotent disposal (and can prevent callers from attempting post-disposal work).
			if(_hasBeenDisposed)
				return;
				
			_hasBeenDisposed = true;
			stableReference = _cancelTokenSource;
			// NB: nullifying the field prevents a later operation from treating it as active.
			_cancelTokenSource = null;
		}
		stableReference?.Cancel();
		stableReference?.Dispose();
		// NB: wait for any extant TryReconnect calls to release the lock before disposing it.
		_reconnectionLock.Wait();
		// NB: we punt on .Dispose in order to let operations queued on .WaitAsync pass through the
		// _hasBeenDisposed completion path instead of hanging indefinitely. Rely on GC to dispose of
		// _reconnectionLock instead of executing deterministic disposal here.
		_reconnectionLock.Release();
	}
}