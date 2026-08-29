using BoundaryModels;

namespace PasteBeside.HandshakeHub;

public interface HandshakeHubService: AsyncInitialization
{
	Task JoinHandshake(Participant localPeer);
	Task AbandonHandshake(Participant localPeer);
}