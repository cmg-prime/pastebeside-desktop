using BoundaryModels;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Options;
using PasteBeside.ClientFriendlyLog;

namespace PasteBeside.HandshakeHub;

public class HubServiceFactory
{
	private readonly SignalRHandshakeHubService _service;

	public HubServiceFactory(IOptions<HubOptions> hubOptions, ClientLogger clientLogger, Participant localPeer)
		=> _service = new SignalRHandshakeHubService(hubOptions, clientLogger, localPeer);

	public HandshakeHubService Create(OnAvailableHandshakes onAvailableHandshakes, OnPeerInitialized onPeerInitialized, OnJoiningHandshake onJoiningHandshake, OnPeerJoiningHandshake onPeerJoiningHandshake, OnAbandonedHandshake onAbandonedHandshake, OnPeerAbandonedHandshake onPeerAbandonedHandshake)
	{
		_service.RegisterCallbacks(onAvailableHandshakes, onPeerInitialized, onJoiningHandshake, onPeerJoiningHandshake, onAbandonedHandshake, onPeerAbandonedHandshake);
		return _service;
	}

	private class SignalRHandshakeHubService: HandshakeHubService
	{
		private readonly ClientLogger _clientLogger;
		private readonly HubConnection _connection;

		private OnAvailableHandshakes? _OnAvailableHandshakes;
		private OnPeerInitialized? _OnPeerInitialized;
		private OnJoiningHandshake? _OnJoiningHandshake;
		private OnPeerJoiningHandshake? _OnPeerJoiningHandshake;
		private OnAbandonedHandshake? _OnAbandonedHandshake;
		private OnPeerAbandonedHandshake? _OnPeerAbandonedHandshake;

		public SignalRHandshakeHubService(IOptions<HubOptions> configOptions, ClientLogger clientLog, Participant localPeer)
		{
			_clientLogger = clientLog;
			_connection = new HubConnectionBuilder()
				.WithUrl($"{configOptions.Value.HubBaseUrl}/handshakeHub")
				.WithAutomaticReconnect()
				.ConfigureLogging(builder => builder.SetMinimumLevel(LogLevel.Error))
				.Build();

			RegisterSignalHandlers();
			Initialization = InitializePeer(localPeer);
		}

		public Task Initialization { get; }

		public void RegisterCallbacks(OnAvailableHandshakes onAvailableHandshakes, OnPeerInitialized onPeerInitialized, OnJoiningHandshake onJoiningHandshake, OnPeerJoiningHandshake onPeerJoiningHandshake, OnAbandonedHandshake onAbandonedHandshake, OnPeerAbandonedHandshake onPeerAbandonedHandshake)
		{
			_OnAvailableHandshakes = onAvailableHandshakes;
			_OnPeerInitialized = onPeerInitialized;
			_OnJoiningHandshake = onJoiningHandshake;
			_OnPeerJoiningHandshake = onPeerJoiningHandshake;
			_OnAbandonedHandshake = onAbandonedHandshake;
			_OnPeerAbandonedHandshake = onPeerAbandonedHandshake;
		}

		public async Task JoinHandshake(Participant localPeer)
		{
			await _clientLogger.Info("Invoking JoinHandshake...");
			await (await GuaranteeConnection()).InvokeAsync(HandshakeHubEndpoint.JoinHandshake, localPeer.HandshakeId, localPeer.Id);
		}

		public async Task AbandonHandshake(Participant localPeer)
		{
			await _clientLogger.Info("Invoking AbandonHandshake...");
			await (await GuaranteeConnection()).InvokeAsync(HandshakeHubEndpoint.AbandonHandshake, localPeer.HandshakeId, localPeer.Id);
		}

		//TODO: add hub events & handlers for RTC negotiation steps.
		private void RegisterSignalHandlers()
		{
			_connection.On(nameof(HandshakeClient.AvailableHandshakesFound), async (IList<string> handshakeIds) 
				=> await _OnAvailableHandshakes!.Invoke(handshakeIds));

			_connection.On(nameof(HandshakeClient.PeerInitialized), async (string handshakeId, Participant participant) => await 
				_OnPeerInitialized!.Invoke(handshakeId, participant));

			_connection.On(nameof(HandshakeClient.JoiningHandshake), async (string handshakeId, Participant peer) => await 
				_OnJoiningHandshake!.Invoke(handshakeId, peer));

			_connection.On(nameof(HandshakeClient.PeerJoiningHandshake), async (Participant peer) 
				=> await _OnPeerJoiningHandshake!.Invoke(peer));

			_connection.On(nameof(HandshakeClient.AbandonedHandshake), async (string handshakeId) 
				=> await _OnAbandonedHandshake!.Invoke(handshakeId));
			_connection.On(nameof(HandshakeClient.PeerAbandonedHandshake), async (string handshakeId) 
				=> await _OnPeerAbandonedHandshake!.Invoke(handshakeId));
		}

		private async Task InitializePeer(Participant localPeer)
		{
			await _clientLogger.Info("Invoking InitializePeer...");
			try
			{
				await (await GuaranteeConnection()).InvokeAsync(HandshakeHubEndpoint.InitializePeer, localPeer.Id, localPeer.DisplayName);            
			}
			catch(Exception e)
			{
				await _clientLogger.Error($"{nameof(InitializePeer)} failed with message: \"{e.Message}\"");
			}
		}

		private async Task<HubConnection> GuaranteeConnection()
		{
			if(_connection.State == HubConnectionState.Disconnected)
				await _connection.StartAsync();

			return _connection;
		}
	}
}