using System.Text.Json.Serialization;

namespace BusinessOS.Restaurant.Application.OperationalData;

public sealed record OperationalBootstrapEnvelope(
    [property: JsonPropertyName("data")] OperationalSnapshot Data);

public sealed record OperationalSnapshot(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("server_time")] DateTimeOffset ServerTime,
    [property: JsonPropertyName("cursor")] long Cursor,
    [property: JsonPropertyName("tenant_id")] string TenantId,
    [property: JsonPropertyName("branches")] IReadOnlyList<BranchSnapshot> Branches,
    [property: JsonPropertyName("staff")] IReadOnlyList<StaffSnapshot> Staff,
    [property: JsonPropertyName("menu")] IReadOnlyList<MenuCategorySnapshot> Menu,
    [property: JsonPropertyName("tables")] IReadOnlyList<DiningTableSnapshot> Tables,
    [property: JsonPropertyName("kitchen")] KitchenSnapshot? Kitchen = null);

public sealed record BranchSnapshot(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("is_active")] bool IsActive);

public sealed record StaffSnapshot(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("public_id")] string PublicId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("is_active")] bool IsActive);

public sealed record MenuCategorySnapshot(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("sort_order")] int SortOrder,
    [property: JsonPropertyName("items")] IReadOnlyList<MenuItemSnapshot> Items);

public sealed record MenuItemSnapshot(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("menu_category_id")] string? MenuCategoryId,
    [property: JsonPropertyName("sku")] string? Sku,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("price")] decimal Price,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("sort_order")] int SortOrder,
    [property: JsonPropertyName("modifier_groups")] IReadOnlyList<ModifierGroupSnapshot> ModifierGroups,
    [property: JsonPropertyName("image_url")] string? ImageUrl = null);

public sealed record ModifierGroupSnapshot(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("min_selections")] int MinSelections,
    [property: JsonPropertyName("max_selections")] int MaxSelections,
    [property: JsonPropertyName("sort_order")] int SortOrder,
    [property: JsonPropertyName("options")] IReadOnlyList<ModifierOptionSnapshot> Options);

public sealed record ModifierOptionSnapshot(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("price_delta")] decimal PriceDelta,
    [property: JsonPropertyName("sort_order")] int SortOrder);

public sealed record DiningTableSnapshot(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("capacity")] int Capacity,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("is_active")] bool IsActive,
    [property: JsonPropertyName("area")] DiningAreaSummary Area,
    [property: JsonPropertyName("branch")] BranchSummary Branch);

public sealed record DiningAreaSummary(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name);

public sealed record BranchSummary(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name);


public sealed record KitchenSnapshot(
    [property: JsonPropertyName("stations")] IReadOnlyList<KitchenStationSnapshot> Stations,
    [property: JsonPropertyName("routes")] IReadOnlyList<MenuItemKitchenRouteSnapshot> Routes);

public sealed record KitchenStationSnapshot(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("branch_id")] string BranchId,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("sort_order")] int SortOrder,
    [property: JsonPropertyName("is_active")] bool IsActive);

public sealed record MenuItemKitchenRouteSnapshot(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("menu_item_id")] string MenuItemId,
    [property: JsonPropertyName("branch_id")] string BranchId,
    [property: JsonPropertyName("kitchen_station_id")] string KitchenStationId);
