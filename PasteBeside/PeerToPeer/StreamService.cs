using System.Net.Sockets;
using System.Text;
using PasteBeside.ClientFriendlyLog;

namespace PasteBeside.PeerToPeer;

public class StreamService
{
	private ClientLogger _logger;
	private readonly SemaphoreSlim _sendLock;

	public StreamService(ClientLogger logger)
	{
		_logger = logger;
		_sendLock = new(1, 1);
	}

	public async Task<(bool IsSucess, string? Message)> TryRead(NetworkStream stream, CancellationToken cancelToken)
	{
		// NB: recall that the header - a 32-bit integer - is 4 bytes.
		var headerReadAttempt = await ReadBytes(stream!, 4, cancelToken);
		if (!headerReadAttempt.IsSuccess) 
			return (false, null);

		var lengthInBytes = BitConverter.ToInt32(headerReadAttempt.Bytes, 0);
		// TODO: arbitrarily set to 100MB. Gather telemetry, adjust accordingly.
		var safeLimitInBytes = 100 * 1024 * 1024;
		if (lengthInBytes < 0 || lengthInBytes > safeLimitInBytes)
			return (false, null);

		var payloadReadAttempt = await ReadBytes(stream!, lengthInBytes, cancelToken);
		if (!payloadReadAttempt.IsSuccess) 
			return (false, null);

		return (true, Encoding.UTF8.GetString(payloadReadAttempt.Bytes));
	}

	public async Task Write(NetworkStream stream, string message, CancellationToken cancelToken)
	{
		var payload = Encoding.UTF8.GetBytes(message);
		//NB: .GetBytes returns byte[4], the length of a 32-bit int; the header contains the payload length.
		var header = BitConverter.GetBytes(payload.Length);

		await _sendLock.WaitAsync(cancelToken);
		try {
			await stream.WriteAsync(header, cancelToken);
			await stream.WriteAsync(payload, cancelToken);
		}
		catch(OperationCanceledException){ }
		catch(Exception e) {
			await _logger.Error($"Error writing to stream: {e.Message}");
		}
		finally {
			_sendLock.Release();
		}
	}

	private static async Task<(bool IsSuccess, byte[] Bytes)> ReadBytes(NetworkStream stream, int byteCount, CancellationToken cancelToken)
	{
		// NB: .ReadAsync returns [0, buffer.Length] bytes - it's just an (ordered) byte stream, which may
		// or may not have access to all the requested bytes at once. (Consider: how many .WriteAsync 
		// operations do we perform to send a message?) We need to loop until completion.
		var buffer = new byte[byteCount];
		try {
			var totalBytesRead = 0;
			while(totalBytesRead < buffer.Length)
			{
				var bytesRead = await stream.ReadAsync(buffer.AsMemory(totalBytesRead), cancelToken);
				// NB: zero bytes read implies the stream has been closed.
				if(bytesRead == 0)
					return (false, buffer);
				
				totalBytesRead += bytesRead;
			}
		} 
		catch (OperationCanceledException)
		{
			throw;
		}
		catch {
			return (false, buffer);
		}

		return (true, buffer);
	}
}