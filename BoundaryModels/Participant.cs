namespace BoundaryModels;

public class Participant
{
    //TODO: rename parameter to just 'id'
    public Participant(string userId, string? displayName = null)
    {
        Id = userId;
        DisplayName = displayName ?? userId;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string? ConnectionId { get; set; }
    //TOOD: not actually nullable
    public string? HandshakeId { get; set; }
}