using CalibraHub.Application.Abstractions.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace CalibraHub.Infrastructure.Messaging;

public sealed class InMemoryMessageBus : IMessageBus
{
    private readonly IServiceScopeFactory _scopeFactory;

    public InMemoryMessageBus(IServiceScopeFactory scopeFactory)
        => _scopeFactory = scopeFactory;

    public async Task PublishAsync<TMessage>(TMessage message, CancellationToken ct = default)
        where TMessage : IMessage
    {
        using var scope = _scopeFactory.CreateScope();
        var handlers = scope.ServiceProvider.GetServices<IMessageHandler<TMessage>>();
        foreach (var handler in handlers)
            await handler.HandleAsync(message, ct);
    }
}
