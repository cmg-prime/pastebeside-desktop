using System.Collections.Concurrent;
using BoundaryModels;
using Microsoft.AspNetCore.SignalR;

namespace Api;

public class HandshakeHub: Hub<HandshakeClient>
{
    private static readonly Lock _joinLock = new(); 
    private static readonly Lock _leaveLock = new(); 
    private static readonly ConcurrentDictionary<string, Participant> _participantsByConnectionId = new();
    private static readonly ConcurrentDictionary<string, Participant> _participantsById = new();
    private static readonly ConcurrentDictionary<string, IDictionary<string, Participant>> _participantsByIdByHandshakeId = new();

    public async Task InitializePeer(string participantId, string participantDisplayName)
    {
        var handshakeIdentifier = HandshakeIdentifier.New();        
        var newParticipant = new Participant(participantId, participantDisplayName)
        {
            ConnectionId = Context.ConnectionId,
            HandshakeId = handshakeIdentifier
        };
        await Clients.Caller.PeerInitialized(handshakeIdentifier, newParticipant);

        _participantsByConnectionId[Context.ConnectionId] = newParticipant;
        _participantsById[participantId] = newParticipant;
        _participantsByIdByHandshakeId[handshakeIdentifier] = new Dictionary<string, Participant>(){ { newParticipant.Id, newParticipant }};

        var handshakeIds = _participantsByIdByHandshakeId.Keys.ToList();
        await Clients.All.AvailableHandshakesFound(handshakeIds);
    }

    public async Task AbandonHandshake(string handshakeId, string participantId)
        => await DisconnectGracefully(Context.ConnectionId, handshakeId, participantId);

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if(_participantsByConnectionId.TryGetValue(Context.ConnectionId, out var participant))
            await DisconnectGracefully(Context.ConnectionId, participant.HandshakeId!, participant.Id);
 
        await base.OnDisconnectedAsync(exception);
    }

    public async Task JoinHandshake(string handshakeId, string participantId)
    {
        //TODO: if there's no record of the joining participant existing, respond with error
        if (!_participantsById.TryGetValue(participantId, out Participant? joiningParticipant))
            return;

        var tasks = new List<Task>();
        Participant? otherParticipant;
        lock (_joinLock)
        {
            var participants = _participantsByIdByHandshakeId[handshakeId];
            //TODO: if we're joining a fully populated or depopulated handshake, respond with error.
            if(participants.Count != 1)
                return;

            //NB: ensure participant can shake no other hands
            if(joiningParticipant.HandshakeId != handshakeId)
                tasks.Add(ExtractParticipantFromHandshake(joiningParticipant.HandshakeId!, participantId));

            //NB: if participant is known to exist, but not on this connection, update the in-memory cache.
            if(joiningParticipant.ConnectionId != Context.ConnectionId)
            {
                _participantsByConnectionId.TryRemove(joiningParticipant.ConnectionId!, out _);
                joiningParticipant.ConnectionId = Context.ConnectionId;
                _participantsByConnectionId[Context.ConnectionId] = joiningParticipant;
            }

            otherParticipant = participants.First().Value;
            joiningParticipant.HandshakeId = handshakeId;                    
            participants[joiningParticipant.Id] = joiningParticipant;
        }

        tasks.Add(Clients.Caller.JoiningHandshake(handshakeId, otherParticipant));
        tasks.Add(Clients.Client(otherParticipant.ConnectionId!).PeerJoiningHandshake(joiningParticipant));    
        await Task.WhenAll(tasks);
    }

    private async Task DisconnectGracefully(string connectionId, string handshakeId, string participantId)
    {
		await ExtractParticipantFromHandshake(handshakeId, participantId);
        lock (_leaveLock)
        {
            _participantsByConnectionId.TryRemove(connectionId, out _);
			_participantsById.TryRemove(participantId, out _);
        }
    }

	private async Task ExtractParticipantFromHandshake(string handshakeId, string participantId)
	{
		var notificationTasks = new List<Task>();
		lock (_leaveLock)
		{
            //TODO: if we're leaving a handshake that doesn't exist, respond with error
            if(!_participantsByIdByHandshakeId.TryGetValue(handshakeId, out var participants))
                return;

            participants.Remove(participantId);
            notificationTasks.Add(Clients.Caller.AbandonedHandshake(handshakeId));
            if(participants.Count < 1)
            {
                _participantsByIdByHandshakeId.TryRemove(handshakeId, out _);
                var handshakeIds = _participantsByIdByHandshakeId.Keys.ToList();
                notificationTasks.Add(Clients.All.AvailableHandshakesFound(handshakeIds));
            }
            else
            {
                var otherMember = participants.Values.First();
                notificationTasks.Add(
                    Clients
                        .Client(otherMember.ConnectionId!)
                        .PeerAbandonedHandshake(handshakeId)
                );
            }
		}
		await Task.WhenAll(notificationTasks);
	}

}