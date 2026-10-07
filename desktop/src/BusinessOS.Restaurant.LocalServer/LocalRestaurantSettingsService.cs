using System.Globalization;
using System.Text.Json;
using BusinessOS.Restaurant.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Restaurant.LocalServer;

public sealed record RestaurantWorkflowSettings(
    bool KitchenQueueEnabled,
    bool PreparingStageEnabled,
    bool ExpoEnabled,
    bool CoursesEnabled,
    bool KotSoundEnabled,
    int KitchenWarningMinutes,
    int KitchenLateMinutes,
    bool RequireManagerApprovalForPostKotVoid,
    string NegativeStockPolicy)
{
    public static RestaurantWorkflowSettings Defaults { get; } = new(
        KitchenQueueEnabled: true,
        PreparingStageEnabled: true,
        ExpoEnabled: false,
        CoursesEnabled: false,
        KotSoundEnabled: true,
        KitchenWarningMinutes: 10,
        KitchenLateMinutes: 20,
        RequireManagerApprovalForPostKotVoid: false,
        NegativeStockPolicy: "block");
}

public sealed record RestaurantWorkflowSettingsUpdate(
    bool KitchenQueueEnabled,
    bool PreparingStageEnabled,
    bool ExpoEnabled,
    bool CoursesEnabled,
    bool KotSoundEnabled,
    int KitchenWarningMinutes,
    int KitchenLateMinutes,
    bool RequireManagerApprovalForPostKotVoid,
    string NegativeStockPolicy = "block");

public sealed class LocalRestaurantSettingsService
{
    public const string KitchenQueueEnabledKey = "kitchen_queue_enabled";
    public const string PreparingStageEnabledKey = "preparing_stage_enabled";
    public const string ExpoEnabledKey = "expo_enabled";
    public const string CoursesEnabledKey = "courses_enabled";
    public const string KotSoundEnabledKey = "kot_sound_enabled";
    public const string KitchenWarningMinutesKey = "kitchen_warning_minutes";
    public const string KitchenLateMinutesKey = "kitchen_late_minutes";
    public const string RequireManagerVoidApprovalKey = "require_manager_approval_post_kot_void";
    public const string LegacyRequireManagerVoidApprovalKey = "require_manager_approval_for_post_kot_void";
    public const string NegativeStockPolicyKey = "negative_stock_policy";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly LocalDatabaseFactory _databaseFactory;

    public LocalRestaurantSettingsService(LocalDatabaseFactory databaseFactory)
    {
        _databaseFactory = databaseFactory;
    }

    public async Task<RestaurantWorkflowSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        await _databaseFactory.EnsureCreatedAsync(cancellationToken);
        await using var db = _databaseFactory.Create();
        return await GetAsync(db, cancellationToken);
    }

    public static async Task<RestaurantWorkflowSettings> GetAsync(
        RestaurantDbContext db,
        CancellationToken cancellationToken = default)
    {
        var values = await db.RestaurantSettings
            .AsNoTracking()
            .ToDictionaryAsync(value => value.Key, value => value.Value, StringComparer.Ordinal, cancellationToken);

        var defaults = RestaurantWorkflowSettings.Defaults;
        return new RestaurantWorkflowSettings(
            Bool(values, KitchenQueueEnabledKey, defaults.KitchenQueueEnabled),
            Bool(values, PreparingStageEnabledKey, defaults.PreparingStageEnabled),
            Bool(values, ExpoEnabledKey, defaults.ExpoEnabled),
            Bool(values, CoursesEnabledKey, defaults.CoursesEnabled),
            Bool(values, KotSoundEnabledKey, defaults.KotSoundEnabled),
            Int(values, KitchenWarningMinutesKey, defaults.KitchenWarningMinutes, 1, 240),
            Int(values, KitchenLateMinutesKey, defaults.KitchenLateMinutes, 1, 480),
            BoolWithLegacy(
                values,
                RequireManagerVoidApprovalKey,
                LegacyRequireManagerVoidApprovalKey,
                defaults.RequireManagerApprovalForPostKotVoid),
            StockPolicy(values, NegativeStockPolicyKey, defaults.NegativeStockPolicy));
    }

    public async Task<RestaurantWorkflowSettings> UpdateAsync(
        RestaurantWorkflowSettingsUpdate update,
        LocalTerminalPrincipal actor,
        CancellationToken cancellationToken = default)
    {
        EnsureManager(actor);

        if (update.KitchenWarningMinutes is < 1 or > 240)
        {
            throw new LocalSyncConflictException("invalid_payload", "Kitchen warning minutes must be between 1 and 240.");
        }

        if (update.KitchenLateMinutes is < 1 or > 480 ||
            update.KitchenLateMinutes < update.KitchenWarningMinutes)
        {
            throw new LocalSyncConflictException(
                "invalid_payload",
                "Kitchen late minutes must be at least the warning threshold and no more than 480.");
        }

        var negativeStockPolicy = NormalizeStockPolicy(update.NegativeStockPolicy);

        await _databaseFactory.EnsureCreatedAsync(cancellationToken);
        await using var db = _databaseFactory.Create();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        await UpsertAsync(db, KitchenQueueEnabledKey, update.KitchenQueueEnabled ? "true" : "false", now, cancellationToken);
        await UpsertAsync(db, PreparingStageEnabledKey, update.PreparingStageEnabled ? "true" : "false", now, cancellationToken);
        await UpsertAsync(db, ExpoEnabledKey, update.ExpoEnabled ? "true" : "false", now, cancellationToken);
        await UpsertAsync(db, CoursesEnabledKey, update.CoursesEnabled ? "true" : "false", now, cancellationToken);
        await UpsertAsync(db, KotSoundEnabledKey, update.KotSoundEnabled ? "true" : "false", now, cancellationToken);
        await UpsertAsync(db, KitchenWarningMinutesKey, update.KitchenWarningMinutes.ToString(CultureInfo.InvariantCulture), now, cancellationToken);
        await UpsertAsync(db, KitchenLateMinutesKey, update.KitchenLateMinutes.ToString(CultureInfo.InvariantCulture), now, cancellationToken);
        await UpsertAsync(db, RequireManagerVoidApprovalKey, update.RequireManagerApprovalForPostKotVoid ? "true" : "false", now, cancellationToken);
        await UpsertAsync(db, NegativeStockPolicyKey, negativeStockPolicy, now, cancellationToken);

        var legacy = await db.RestaurantSettings.SingleOrDefaultAsync(
            value => value.Key == LegacyRequireManagerVoidApprovalKey,
            cancellationToken);
        if (legacy is not null)
        {
            db.RestaurantSettings.Remove(legacy);
        }

        // Flush inside the still-open transaction so the no-tracking snapshot sees
        // newly inserted canonical settings rather than falling back to legacy rows/defaults.
        await db.SaveChangesAsync(cancellationToken);
        var snapshot = await GetAsync(db, cancellationToken);
        db.Changes.Add(new LocalChange
        {
            EntityType = "restaurant_settings",
            EntityId = "workflow",
            Operation = "upsert",
            OwnerUserId = null,
            DataJson = JsonSerializer.Serialize(ToPayload(snapshot), JsonOptions),
            OccurredAtUtc = now,
        });

        LocalCloudOutboxWriter.Enqueue(
            db,
            actor,
            "restaurant.settings.update",
            "restaurant_settings",
            "workflow",
            ToPayload(snapshot),
            now);

        LocalOperationsControlService.AddAudit(
            db,
            actor,
            "settings",
            "restaurant.workflow_settings_updated",
            null,
            "restaurant_settings",
            "workflow",
            ToPayload(snapshot));

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return snapshot;
    }

    public static object ToPayload(RestaurantWorkflowSettings settings) => new
    {
        kitchen_queue_enabled = settings.KitchenQueueEnabled,
        preparing_stage_enabled = settings.PreparingStageEnabled,
        expo_enabled = settings.ExpoEnabled,
        courses_enabled = settings.CoursesEnabled,
        kot_sound_enabled = settings.KotSoundEnabled,
        kitchen_warning_minutes = settings.KitchenWarningMinutes,
        kitchen_late_minutes = settings.KitchenLateMinutes,
        require_manager_approval_post_kot_void = settings.RequireManagerApprovalForPostKotVoid,
        negative_stock_policy = settings.NegativeStockPolicy,
    };

    private static async Task UpsertAsync(
        RestaurantDbContext db,
        string key,
        string value,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var row = await db.RestaurantSettings.SingleOrDefaultAsync(x => x.Key == key, cancellationToken);
        if (row is null)
        {
            row = new LocalRestaurantSetting
            {
                Key = key,
                Value = value,
                Source = "local",
                UpdatedAtUtc = now,
            };
            db.RestaurantSettings.Add(row);
            return;
        }

        row.Value = value;
        row.Source = "local";
        row.UpdatedAtUtc = now;
    }

    private static bool Bool(IReadOnlyDictionary<string, string> values, string key, bool fallback) =>
        values.TryGetValue(key, out var raw) && bool.TryParse(raw, out var value) ? value : fallback;

    private static bool BoolWithLegacy(
        IReadOnlyDictionary<string, string> values,
        string key,
        string legacyKey,
        bool fallback)
    {
        if (values.TryGetValue(key, out var raw) && bool.TryParse(raw, out var value))
        {
            return value;
        }

        return values.TryGetValue(legacyKey, out var legacyRaw) &&
               bool.TryParse(legacyRaw, out var legacyValue)
            ? legacyValue
            : fallback;
    }

    private static string StockPolicy(
        IReadOnlyDictionary<string, string> values,
        string key,
        string fallback) =>
        values.TryGetValue(key, out var raw)
            ? NormalizeStockPolicy(raw)
            : fallback;

    public static string NormalizeStockPolicy(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized is "block" or "warn" or "allow")
        {
            return normalized;
        }

        throw new LocalSyncConflictException(
            "invalid_payload",
            "negative_stock_policy must be block, warn or allow.");
    }

    private static int Int(
        IReadOnlyDictionary<string, string> values,
        string key,
        int fallback,
        int minimum,
        int maximum) =>
        values.TryGetValue(key, out var raw) &&
        int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, minimum, maximum)
            : fallback;

    private static void EnsureManager(LocalTerminalPrincipal actor)
    {
        if (actor.UserRole is not ("owner" or "admin" or "manager"))
        {
            throw new LocalSyncConflictException(
                "forbidden",
                "Only an owner, admin or manager can change restaurant workflow settings.",
                "rejected");
        }
    }
}
