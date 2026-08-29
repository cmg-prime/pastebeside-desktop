using BoundaryModels;
using PasteBeside.ClientFriendlyLog;
using PasteBeside.HandshakeHub;

namespace PasteBeside;

internal partial record MainModel
{
	private readonly HandshakeHubService _hubService;

	public MainModel(ClientLogger clientLog, HubServiceFactory hubServiceFactory, Participant localPeer)
	{
		_hubService = hubServiceFactory.Create(
			async handshakeIds => await AvailableHandshakes!.UpdateAsync(_ => handshakeIds),
			async (handshakeId, localParticipant) => 
			{
				await _hubService!.Initialization;
				await AvailableHandshakes!.UpdateAsync(existing => [..existing!, handshakeId]);
				await LocalParticipant!.UpdateAsync((current) => 
					current! with { 
						HandshakeId = localParticipant.HandshakeId,
						ConnectionId = localParticipant.ConnectionId 
					});
				await clientLog.Success($"Initialized handshake {localParticipant.HandshakeId}!");
			},
			async (handshakeId, remotePeer) =>
			{
				await _hubService!.Initialization;
				await LocalParticipant!.UpdateAsync(current => current! with { HandshakeId = handshakeId });
				await clientLog.Success($"Joining handshake ${handshakeId} with peer ${remotePeer.DisplayName}!");
				//TODO: process RTC offer, send answer.
			},
			async remotePeer =>
			{
				await _hubService!.Initialization;
				await clientLog.Success($"Peer ${remotePeer.DisplayName} joined handshake!");
				//TODO: send RTC offer.
			},
			handshakeId => OnAbandonedDelegate(handshakeId, $"Handshake ${handshakeId} abandoned."),
			handshakeId => OnAbandonedDelegate(handshakeId, $"Handshake ${handshakeId} abandoned by remote peer.")
		);
		
		Messages = clientLog.Messages;
		LocalParticipant = State<Participant>.Value(this, () => localPeer);
		AvailableHandshakes = State<IList<string>>.Value(this, () => []);

		async Task OnAbandonedDelegate(string handshakeId, string logMessage)
		{
			await _hubService!.Initialization;
			await AvailableHandshakes!.UpdateAsync(existing =>
			{
				existing!.Remove(handshakeId);
				return existing;
			});
			await clientLog.Info($"Handshake ${handshakeId} abandoned by remote peer.");
		};
	}

	public IState<IReadOnlyList<ClientLogMessage>> Messages { get; }
	public IState<Participant> LocalParticipant { get; }
	public IState<IList<string>> AvailableHandshakes { get; }

}
