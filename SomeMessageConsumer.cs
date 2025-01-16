using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace OpenTelemetryPoc;

public class SomeMessageConsumer(ILogger<SomeMessageConsumer> logger, AuditContext auditContext) : IConsumer<SomeMessage>
{
    public async Task Consume(ConsumeContext<SomeMessage> context)
    {
        await Task.Delay(666, context.CancellationToken);
        logger.LogInformation("Consuming some message: {@Message}", context.Message);
        await Task.Delay(42, context.CancellationToken);
        var count = await auditContext.AuditEntries.CountAsync(cancellationToken: context.CancellationToken);
        logger.LogInformation("{Count} audit entries in the database at the moment", count);
    }
}

public record SomeMessage
{
    public DateOnly MaxTemperatureDate { get; init; }
    public int MaxTemperature { get; init; }
}
