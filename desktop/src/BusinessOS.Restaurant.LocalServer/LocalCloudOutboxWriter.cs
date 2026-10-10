using BusinessOS.Restaurant.Licensing;
using System.Text.Json;
using BusinessOS.Restaurant.Persistence;

namespace BusinessOS.Restaurant.LocalServer;

public static class LocalCloudOutboxWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void Enqueue(
        RestaurantDbContext db,
        LocalTerminalPrincipal actor,
        string operation,
        string entityType,
        string localEntityId,
        object payload,
        DateTimeOffset? occurredAt = null,
        string? mutationId = null)
    {
        // Standalone orders must never enter a queue that could later transmit
        // restaurant orders or financial records to the web.
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var activation = new WindowsActivationStore().LoadAsync().GetAwaiter().GetResult();
                if (DesktopOperatingMode.IsStandalone(activation))
                    return;
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                // Licensing failures are handled by application startup; local
                // restaurant operations must not be interrupted by the outbox.
            }
        }

        var id = string.IsNullOrWhiteSpace(mutationId)
            ? Guid.CreateVersion7().ToString("N")
            : mutationId.Trim();

        if (db.CloudOutbox.Local.Any(value => value.Id == id))
        {
            return;
        }

        db.CloudOutbox.Add(new LocalCloudOutboxMutation
        {
            Id = id,
            Operation = operation,
            EntityType = entityType,
            LocalEntityId = localEntityId,
            ActorUserId = actor.UserId,
            ActorPublicId = actor.UserPublicId,
            PayloadJson = JsonSerializer.Serialize(payload, JsonOptions),
            Status = "pending",
            Attempts = 0,
            OccurredAtUtc = occurredAt ?? DateTimeOffset.UtcNow,
        });
    }
}
