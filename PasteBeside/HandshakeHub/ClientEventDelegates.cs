using BoundaryModels;

namespace PasteBeside.HandshakeHub;

public delegate Task OnAvailableHandshakes(IList<string> handshakeIds);
public delegate Task OnPeerInitialized(string handshakeId, Participant participant);
public delegate Task OnJoiningHandshake(string handshakeId, Participant participant);
public delegate Task OnPeerJoiningHandshake(Participant participant);
public delegate Task OnAbandonedHandshake(string handshakeId);
public delegate Task OnPeerAbandonedHandshake(string handshakeId);