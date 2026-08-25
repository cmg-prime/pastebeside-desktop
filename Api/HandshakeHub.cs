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
        var random = new Random();
        var adjectives = new List<string>(["Big", "Small"]);
        var nouns = new List<string>(["Thing", "Trinket"]);
        var randomAdjectiveIndex = random.Next(0, adjectives.Count);
        var randomNounIndex = random.Next(0, nouns.Count);

        var adjective = adjectives[randomAdjectiveIndex];
        var noun = nouns[randomNounIndex];
        var handshakeIdentifier = $"{adjective}-{noun}-{random.Next(0, 10000)}";

        //TODO: factor out shared logic
        var newParticipant = new Participant(participantId, participantDisplayName)
        {
            ConnectionId = Context.ConnectionId,
            HandshakeId = handshakeIdentifier
        };
        //TODO: rename ReadyForHandshake to HandshakeInitialized
        await Clients.Caller.ReadyForHandshake(handshakeIdentifier, newParticipant);

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

    public async Task JoinHandshake(string handshakeId, string participantId, string participantDisplayName)
    {
        //TODO: call JoiningHandshake/PeerJoiningHandshake
    }

}