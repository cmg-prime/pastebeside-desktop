using System.Net.Sockets;
using PasteBeside.ClientFriendlyLog;
using PasteBeside.Eventing;

namespace PasteBeside.PeerToPeer;

public class MessageServiceFactory(ClientLogger _logger, StreamService _streamService, EventBus eventBus)
{
	public MessageService Create(TcpClient client, CancellationToken cancelToken) 
		=> new TcpMessageService(_logger, _streamService, client, eventBus, cancelToken);

	private class TcpMessageService : MessageService
	{
		private readonly ClientLogger _logger;
		private readonly StreamService _streamService;
		private readonly TcpClient _client;
		private readonly CancellationTokenSource _cancelTokenSource;
		private readonly EventBus _eventBus;

		public TcpMessageService(ClientLogger logger, StreamService streamService, TcpClient client, EventBus eventBus, CancellationToken cancelToken)
		{
			_logger = logger;
			_streamService = streamService;
			_client = client;
			_eventBus = eventBus;
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

					_eventBus.OnMessageReceived(readAttempt.Message!);
				}
			}
			catch (OperationCanceledException) { 
				await _logger.Info("Canceled peer connection loop.");
			}
			catch (Exception e) {
				await _logger.Error($"Problem listening for peer connection: {e.Message}");
			}
			finally
			{
				stream.Dispose();	
			}
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