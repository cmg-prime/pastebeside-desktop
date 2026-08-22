namespace PasteBeside.HandshakeHub;

public static class HandshakeHubEvent
{
    public const string AvailableHandshakesFound = "AvailableHandshakesFound";
    public const string ReadyForHandshake = "AvailableForConnection";
    public const string JoiningHandshake = "JoiningHandshake";
    public const string PeerJoiningHandshake = "PeerJoiningHandshake";
    public const string AbandonedHandshake = "AbandonedHandshake";
    public const string PeerAbandonedHandshake = "PeerAbandonedHandshake";
}