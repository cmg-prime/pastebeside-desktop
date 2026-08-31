using BoundaryModels;
using PasteBeside.ClientFriendlyLog;
using PasteBeside.HandshakeHub;

namespace PasteBeside;

internal partial record MainModel
{
	private readonly HandshakeHubService _hubService;

	public MainModel(ClientLogger clientLogger, HubServiceFactory hubServiceFactory, Participant localPeer)
	{
		_hubService = hubServiceFactory.Create(
			async handshakeIds => await AvailableHandshakes!.UpdateAsync(_ => handshakeIds),
			async (handshakeId, localParticipant) => 
			{
				await _hubService!.Initialization;
				await LocalPeer!.UpdateAsync((current) => 
					current! with { 
						HandshakeId = localParticipant.HandshakeId,
						ConnectionId = localParticipant.ConnectionId 
					});
				await clientLogger.Success($"Initialized handshake {localParticipant.HandshakeId}!");
			},
			async (handshakeId, remotePeer) =>
			{
				await _hubService!.Initialization;
				await LocalPeer!.UpdateAsync(current => current! with { HandshakeId = handshakeId });
				await RemotePeer!.UpdateAsync(current => remotePeer);

				await clientLogger.Success($"Joining handshake ${handshakeId} with peer ${remotePeer.DisplayName}!");
				//TODO: process RTC offer, send answer.
			},
			async remotePeer =>
			{
				await _hubService!.Initialization;
				await RemotePeer!.UpdateAsync(current => remotePeer);

				await clientLogger.Success($"Peer ${remotePeer.DisplayName} joined handshake!");
				//TODO: send RTC offer.
			},
			async handshakeId => {
				await OnAbandonedDelegate(handshakeId, $"Handshake ${handshakeId} abandoned.");
				await LocalPeer!.UpdateAsync(peer =>
				{
					peer!.HandshakeId = null;
					return peer;
				});
			},
			async handshakeId => await OnAbandonedDelegate(handshakeId, $"Handshake ${handshakeId} abandoned by remote peer.")
		);
		
		Messages = clientLogger.Messages;
		LocalPeer = State<Participant>.Value(this, () => localPeer);
		RemotePeer = State<Participant>.Empty(this);
		AvailableHandshakes = State<IList<string>>.Value(this, () => []);

		async Task OnAbandonedDelegate(string handshakeId, string logMessage)
		{
			await _hubService!.Initialization;
			await AvailableHandshakes!.UpdateAsync(existing =>
			{
				existing!.Remove(handshakeId);
				return existing;
			});
			await RemotePeer!.UpdateAsync(peer => null);
			await clientLogger.Info(logMessage);
		};
	}

	public IState<IReadOnlyList<ClientLogMessage>> Messages { get; }
	public IState<Participant> LocalPeer { get; }
	public IState<Participant> RemotePeer { get; }
	public IState<IList<string>> AvailableHandshakes { get; }

}
