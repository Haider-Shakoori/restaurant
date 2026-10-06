using System.Text.Json.Serialization;

namespace BusinessOS.Restaurant.Authentication;

public sealed record LoginRequest(
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("password")] string Password,
    [property: JsonPropertyName("device_name")] string DeviceName);

public sealed record AuthUser(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("public_id")] string PublicId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("role")] string Role);

public sealed record LoginResponse(
    [property: JsonPropertyName("token_type")] string TokenType,
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("user")] AuthUser User,
    [property: JsonPropertyName("tenant_id")] string TenantId);

public sealed record AuthSession(
    string TenantBaseUrl,
    string AccessToken,
    AuthUser User,
    string TenantId,
    DateTimeOffset SignedInAt);
