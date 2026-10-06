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
