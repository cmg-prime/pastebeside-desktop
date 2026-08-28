namespace BoundaryModels;

public record Participant
{
    public Participant(string id, string? displayName = null)
    {
        Id = id;
        DisplayName = displayName ?? id;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string? ConnectionId { get; set; }
    public string? HandshakeId { get; set; }
}