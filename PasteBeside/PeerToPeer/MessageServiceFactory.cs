using System.Net.Sockets;
using System.Text;
using PasteBeside.ClientFriendlyLog;

namespace PasteBeside.PeerToPeer;

public class MessageServiceFactory(ClientLogger _logger, StreamService _streamService)
{
	public MessageService Create(TcpClient client, CancellationToken cancelToken) 
		=> new TcpMessageService(_logger, _streamService, client, cancelToken);

	private class TcpMessageService : MessageService
	{
		private readonly ClientLogger _logger;
		private readonly StreamService _streamService;
		private readonly TcpClient _client;
		private readonly CancellationTokenSource _cancelTokenSource;

		public TcpMessageService(ClientLogger logger, StreamService streamService, TcpClient client, CancellationToken cancelToken)
		{
			_logger = logger;
			_streamService = streamService;
			_client = client;
			_cancelTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancelToken);
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
					var readAttempt =  await _streamService.TryRead(stream, _cancelTokenSource.Token);
					if(!readAttempt.IsSucess)
						break;

					await _logger.Success($"Message received: {readAttempt.Message}");
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

			await _streamService.Write(_client.GetStream(), message, _cancelTokenSource.Token);
		}

		public void Dispose()
		{
			_client.Close();
			_cancelTokenSource.Cancel();
			_cancelTokenSource.Dispose();
		}
	}
}