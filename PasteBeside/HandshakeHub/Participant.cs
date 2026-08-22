namespace PasteBeside.HandshakeHub;

public class Participant
{
    public Participant(string userId, string? displayName = null)
    {
        Id = userId;
        DisplayName = displayName ?? userId;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string? ConnectionId { get; set; }
    public string? HandshakeId { get; set; }
}