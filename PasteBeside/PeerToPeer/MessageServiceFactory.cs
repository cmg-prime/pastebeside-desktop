using System.Net.Sockets;
using System.Text;
using PasteBeside.ClientFriendlyLog;

namespace PasteBeside.PeerToPeer;

public class MessageServiceFactory(ClientLogger _logger)
{
	public MessageService Create(TcpClient client, CancellationToken cancelToken) 
		=> new TcpMessageService(_logger, client, cancelToken);

	private class TcpMessageService : MessageService
	{
		private readonly ClientLogger _logger;
		private readonly SemaphoreSlim _sendLock;
		private readonly TcpClient _client;
		private readonly CancellationTokenSource _cancelTokenSource;

		public TcpMessageService(ClientLogger logger, TcpClient client, CancellationToken cancelToken)
		{
			_logger = logger;
			_client = client;
			_cancelTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancelToken);
			_sendLock = new(1, 1);
		}

		public async Task BeginListening()
		{
			var stream = _client!.GetStream();
			await _logger.Success($"Connected to remote peer!");
			try
			{
				await _logger.Info($"Listening for messages...");
				while (!_cancelTokenSource.IsCancellationRequested)
				{
					// NB: recall that the header - a 32-bit integer - is 4 bytes.
					var headerReadAttempt = await ReadBytes(stream!, 4);
					if (!headerReadAttempt.IsSuccess) 
						break;

					var lengthInBytes = BitConverter.ToInt32(headerReadAttempt.Bytes, 0);
					// TODO: arbitrarily set to 100MB. Gather telemetry, adjust accordingly.
					var safeLimitInBytes = 100 * 1024 * 1024;
					if (lengthInBytes < 0 || lengthInBytes > safeLimitInBytes)
						break;

					var payloadReadAttempt = await ReadBytes(stream!, lengthInBytes);
					if (!payloadReadAttempt.IsSuccess) 
						break;

					var message = Encoding.UTF8.GetString(payloadReadAttempt.Bytes);
					await _logger.Success($"Message received: {message}");
				}
			}
			catch (OperationCanceledException) { 
				await _logger.Info("Canceled peer connection loop.");
			}
			catch (Exception e) {
				await _logger.Error($"Problem listening for peer connection: {e.Message}");
			}

			stream.Dispose();
		}

		public async Task Send(string message)
		{
			if (!_client.Connected) 
				return;

			var payload = Encoding.UTF8.GetBytes(message);
			//NB: .GetBytes returns byte[4]; that's the header.
			var header = BitConverter.GetBytes(payload.Length);
			var stream = _client!.GetStream();

			await _sendLock.WaitAsync();
			try {
				await stream.WriteAsync(header);
				await stream.WriteAsync(payload);
			} 
			catch (Exception e) {
				await _logger.Error($"Error writing to peer: {e.Message}");
			}
			finally {
				_sendLock.Release();
				stream.Dispose();
			}
		}

		private async Task<(bool IsSuccess, byte[] Bytes)> ReadBytes(NetworkStream stream, int byteCount)
		{
			var buffer = new byte[byteCount];
			try {
				var bytesRead = await stream.ReadAsync(buffer, _cancelTokenSource.Token);
				if (bytesRead != byteCount) 
					return (false, buffer);
			} 
			catch {
				return (false, buffer);
			}

			return (true, buffer);
		}

		public void Dispose()
		{
			_client.Close();
			_sendLock.Dispose();
			_cancelTokenSource.TearDown();
		}
	}
}