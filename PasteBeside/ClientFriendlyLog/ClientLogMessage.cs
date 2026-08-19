namespace PasteBeside.ClientFriendlyLog;

public record ClientLogMessage(string Text, ClientLogType Type, DateTime LogTime)
{ }