namespace PasteBeside;

public record ClientLogMessage(string Text, ClientLogType Type, DateTime LogTime)
{ }