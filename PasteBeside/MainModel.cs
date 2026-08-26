using BoundaryModels;
using PasteBeside.ClientFriendlyLog;
using PasteBeside.HandshakeHub;

namespace PasteBeside;

internal partial record MainModel
{
	public MainModel(ClientLog clientLog, HandshakeHubService hubService)
	{
		Messages = clientLog.Messages;
		LocalParticipant = State.Value(this, () => hubService.LocalParticipant);
	}

	public IState<IReadOnlyList<ClientLogMessage>> Messages { get; }
	public IState<Participant> LocalParticipant { get; }

}
