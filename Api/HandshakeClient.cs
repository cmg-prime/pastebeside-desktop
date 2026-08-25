using BoundaryModels;

namespace Api;

public interface HandshakeClient
{
    Task AvailableHandshakesFound(IList<string> handshakeIds);
    Task HandshakeInitialized(string handshakeId, Participant localPeer);
    Task JoiningHandshake(string handshakeId, Participant remotePeer);
    Task PeerJoiningHandshake(Participant remotePeer);
    Task AbandonedHandshake(string handshakeId);
    Task PeerAbandonedHandshake(string handshakeId);
}