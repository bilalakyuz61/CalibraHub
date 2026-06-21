namespace CalibraHub.Application.Abstractions.Messaging;

public interface IMessage { }

public interface IMessageBus
{
    Task PublishAsync<TMessage>(TMessage message, CancellationToken ct = default)
        where TMessage : IMessage;
}

public interface IMessageHandler<TMessage> where TMessage : IMessage
{
    Task HandleAsync(TMessage message, CancellationToken ct = default);
}
