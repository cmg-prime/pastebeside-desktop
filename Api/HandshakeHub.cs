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

    public async Task InitializeHandshake(string participantId, string participantDisplayName)
    {
        var handshakeIdentifier = GenerateHandshakeIdentifier();        
        var newParticipant = new Participant(participantId, participantDisplayName)
        {
            ConnectionId = Context.ConnectionId,
            HandshakeId = handshakeIdentifier
        };
        //TODO: rename ReadyForHandshake to HandshakeInitialized
        await Clients.Caller.HandshakeInitialized(handshakeIdentifier, newParticipant);

        _participantsByConnectionId[Context.ConnectionId] = newParticipant;
        _participantsById[participantId] = newParticipant;
        _participantsByIdByHandshakeId[handshakeIdentifier] = new Dictionary<string, Participant>(){ { newParticipant.Id, newParticipant }};

        var handshakeIds = _participantsByIdByHandshakeId.Keys.ToList();
        await Clients.All.AvailableHandshakesFound(handshakeIds);
    }

    public async Task AbandonHandshake(string handshakeId, string participantId)
    {
        //TODO: call AbandonedHandshake/PeerAbandonedHandshake
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
                tasks.Add(CancelHandshake(joiningParticipant.HandshakeId!, participantId));

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

	private async Task CancelHandshake(string handshakeId, string participantId)
	{
		var notificationTasks = new List<Task>();
		lock (_leaveLock)
		{
            //TODO: if we're leaving a handshake that doesn't exist, respond with error
            if(!_participantsByIdByHandshakeId.TryGetValue(handshakeId, out var participants))
                return;

            participants.Remove(participantId);
            var leavingParticipant = _participantsById[participantId];
            leavingParticipant.HandshakeId = GenerateHandshakeIdentifier();
            _participantsByIdByHandshakeId[leavingParticipant.HandshakeId] = new Dictionary<string, Participant>(){ { leavingParticipant.Id, leavingParticipant }};
            notificationTasks.Add(Clients.Caller.AbandonedHandshake(handshakeId));
            notificationTasks.Add(Clients.Caller.HandshakeInitialized(leavingParticipant.HandshakeId, leavingParticipant));

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

    private static string GenerateHandshakeIdentifier()
    {
        var random = new Random();
        var adjectives = new List<string>(["Big", "Small"]);
        var nouns = new List<string>(["Thing", "Trinket"]);
        var randomAdjectiveIndex = random.Next(0, adjectives.Count);
        var randomNounIndex = random.Next(0, nouns.Count);

        var adjective = adjectives[randomAdjectiveIndex];
        var noun = nouns[randomNounIndex];
        return $"{adjective}-{noun}-{random.Next(0, 10000)}";
    }

}