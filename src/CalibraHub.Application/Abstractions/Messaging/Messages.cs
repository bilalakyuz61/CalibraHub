namespace CalibraHub.Application.Abstractions.Messaging;

public sealed record WhatsAppMessageReceived : IMessage
{
    public string?  ContactPhone { get; init; }
    public string?  Body        { get; init; }
    public bool     IsIncoming  { get; init; }
    public string?  MediaType   { get; init; }
    public string?  BridgeMsgId { get; init; }
    public DateTime At          { get; init; }
}
