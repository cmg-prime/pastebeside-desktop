using BoundaryModels;
using PasteBeside.ClientFriendlyLog;
using PasteBeside.HandshakeHub;

namespace PasteBeside;

internal partial record MainModel
{
	private readonly ClientLogger _clientLogger;
	private readonly HandshakeHubService _hubService;

	public MainModel(ClientLogger clientLogger, HubServiceFactory hubServiceFactory, Participant localPeer)
	{
		_clientLogger = clientLogger;
		_hubService = hubServiceFactory.Create(
			async handshakeIds =>
			{
				var localPeerHandshakeId = (await LocalPeer!)!.HandshakeId!;
				var viewModels = handshakeIds.Select(handshakeId 
					=> new AvailableHandshakeViewModel(handshakeId, localPeerHandshakeId));
				await AvailableHandshakes!.UpdateAsync(_ => [.. viewModels]);
			},
			async (handshakeId, localParticipant) => 
			{
				await _hubService!.Initialization;
				await LocalPeer!.UpdateAsync((current) => 
					current! with { 
						HandshakeId = localParticipant.HandshakeId,
						ConnectionId = localParticipant.ConnectionId 
					});
				await _clientLogger.Success($"Initialized handshake {localParticipant.HandshakeId}!");
			},
			async (handshakeId, remotePeer) =>
			{
				await _hubService!.Initialization;
				await LocalPeer!.UpdateAsync(current => current! with { HandshakeId = handshakeId });
				await RemotePeer!.UpdateAsync(current => remotePeer);

				await _clientLogger.Success($"Joining handshake {handshakeId} with peer {remotePeer.DisplayName}!");
				//TODO: process RTC offer, send answer.
			},
			async remotePeer =>
			{
				await _hubService!.Initialization;
				await RemotePeer!.UpdateAsync(current => remotePeer);

				await _clientLogger.Success($"Peer {remotePeer.DisplayName} joined handshake!");
				//TODO: send RTC offer.
			},
			async handshakeId => {
				await OnAbandonedDelegate(handshakeId, $"Handshake {handshakeId} abandoned.");
				await LocalPeer!.UpdateAsync(peer =>
				{
					peer!.HandshakeId = null;
					return peer;
				});
			},
			async handshakeId => await OnAbandonedDelegate(handshakeId, $"Handshake {handshakeId} abandoned by remote peer.")
		);
		
		Messages = _clientLogger.Messages;
		LocalPeer = State<Participant>.Value(this, () => localPeer);
		RemotePeer = State<Participant>.Empty(this);
		AvailableHandshakes = State<IList<AvailableHandshakeViewModel>>.Value(this, () => []);

		async Task OnAbandonedDelegate(string handshakeId, string logMessage)
		{
			await _hubService!.Initialization;
			await AvailableHandshakes!.UpdateAsync(existing =>
			{
				var targetModel = existing!.First(model => model.AvailableHandshakeId == handshakeId);
				existing!.Remove(targetModel);
				return existing;
			});
			await RemotePeer!.UpdateAsync(peer => null);
			await _clientLogger.Info(logMessage);
		};
	}

	public IState<IReadOnlyList<ClientLogMessage>> Messages { get; }
	public IState<Participant> LocalPeer { get; }
	public IState<Participant> RemotePeer { get; }
	public IState<IList<AvailableHandshakeViewModel>> AvailableHandshakes { get; }

	public async Task ConnectToHandshake(AvailableHandshakeViewModel viewModel)
	{
		if (viewModel.IsCurrentActiveHandshake)
		{
			await _clientLogger.Info($"Already connected to handshake '{viewModel.AvailableHandshakeId}'.");
			return;
		}
		
		var localPeer = await LocalPeer;
		await _hubService.JoinHandshake(localPeer!);
	}

}
