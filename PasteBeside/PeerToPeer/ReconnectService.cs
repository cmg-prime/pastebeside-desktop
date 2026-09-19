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

	public ReconnectService(ClientLogger logger, LastConnectedPeerRepository lastConnectedPeerRepo, DiscoveredPeerRepository discoveryRepo, OutgoingConnectionCommand makeOutgoingConnection)
	{
		_logger = logger;
		_reconnectionLock = new(1,1);
		_lastConnectedPeerRepo = lastConnectedPeerRepo;
		_discoveryRepo = discoveryRepo;
		_outgoingConnectionCommand = makeOutgoingConnection;
	}

	private CancellationTokenSource? _cancelTokenSource;
	private Task<TcpClient?>? _reconnectTask;

	// TODO: callers will then need to InitializeMessageChannel all on their own!
	public async Task<TcpClient?> Reconnect(CancellationToken[] tokens)
	{
		// NB: guard against dangerous usage (called before initialization or after disposal)
		if (tokens.Length < 1 || _lastConnectedPeerRepo.Get() is null)
			return null;

		if (_reconnectTask is { IsCompleted: false })
			return null;

		_cancelTokenSource?.Cancel();
		_cancelTokenSource?.Dispose();
		_cancelTokenSource = CancellationTokenSource.CreateLinkedTokenSource(tokens);
		_reconnectTask = TryReconnect(_cancelTokenSource.Token);
		return await _reconnectTask;
	}

	private async Task<TcpClient?> TryReconnect(CancellationToken cancelToken)
	{
		// NB: need to try-catch entering the lock separately - otherwise we risk entering
		// a finally block that would throw off the semaphore count.
		try
		{
			await _reconnectionLock.WaitAsync(cancelToken);
		}
		catch (OperationCanceledException)
		{
			return null;
		}
		
		try
		{
			var peer = _lastConnectedPeerRepo.Get();
			if(peer is null)
				return null;

			var delay = TimeSpan.FromSeconds(1);
			var timeoutLimitInSeconds = 10;
			while (!cancelToken.IsCancellationRequested && delay.TotalSeconds < timeoutLimitInSeconds)
			{
				await _logger.Info("Attempting to reconnect...");
				var client = await _outgoingConnectionCommand.Execute(peer.Endpoint, cancelToken);
				if (client?.Connected ?? false) {
					_discoveryRepo.AddPeer(peer);
					await _logger.Success("Reconnected to peer!");
					return client;
				}

				// TODO: why add this inner try-catch? Why not catch this exception in the outer?
				try {
					await Task.Delay(delay, cancelToken);
				}
				catch (OperationCanceledException) {
					return null;
				}

				delay = TimeSpan.FromSeconds(delay.TotalSeconds * 1.5);
			}
			// NB: handle a retry timeout separately from a cancellation request.
			if (delay.TotalSeconds >= timeoutLimitInSeconds)
			{
				if (ReferenceEquals(_lastConnectedPeerRepo.Get(), peer))
					_lastConnectedPeerRepo.Reset();

				await _logger.Error("Unable to reconnect.");	
			}
			return null;
		}
		finally
		{
			_reconnectionLock.Release();
		}
	}

	public void Dispose()
	{
		_reconnectionLock.Dispose();
		_cancelTokenSource?.Cancel();
		_cancelTokenSource?.Dispose();
	}
}