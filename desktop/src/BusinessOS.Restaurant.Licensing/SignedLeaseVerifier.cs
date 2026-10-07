using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NSec.Cryptography;

namespace BusinessOS.Restaurant.Licensing;

public sealed class SignedLeaseVerifier
{
    public LeaseSnapshot Verify(SignedLease lease, string publicKey)
    {
        if (!string.Equals(lease.Algorithm, "Ed25519", StringComparison.Ordinal))
        {
            throw new CryptographicException("The offline lease uses an unsupported signature algorithm.");
        }

        var canonical = Canonicalize(lease.Payload);
        var signature = DecodeBase64Url(lease.Signature);
        var publicKeyBytes = DecodeBase64Url(publicKey);
        var algorithm = SignatureAlgorithm.Ed25519;

        if (signature.Length != algorithm.SignatureSize ||
            publicKeyBytes.Length != algorithm.PublicKeySize)
        {
            throw new CryptographicException("The offline lease signature material is invalid.");
        }

        var key = PublicKey.Import(algorithm, publicKeyBytes, KeyBlobFormat.RawPublicKey);

        if (!algorithm.Verify(key, canonical, signature))
        {
            throw new CryptographicException("The offline lease signature is invalid.");
        }

        var root = lease.Payload;

        var snapshot = new LeaseSnapshot(
            ReadInt(root, "schema_version"),
            ReadString(root, "lease_id"),
            ReadString(root, "key_id"),
            ReadString(root, "tenant_id"),
            ReadString(root, "business_id"),
            ReadString(root, "subscription_id"),
            ReadInt(root, "license_version"),
            ReadString(root, "device_id"),
            ReadString(root, "device_uid"),
            ReadString(root.GetProperty("plan"), "code"),
            ReadString(root.GetProperty("plan"), "name"),
            ReadDate(root, "issued_at"),
            ReadDate(root, "offline_valid_until"),
            ReadDate(root, "subscription_ends_at"),
            root.GetProperty("features").Clone(),
            ReadOptionalInt(root, "mobile_device_limit"));

        if (!string.Equals(snapshot.KeyId, lease.KeyId, StringComparison.Ordinal))
        {
            throw new CryptographicException("The offline lease key identifier does not match its signature envelope.");
        }

        if (snapshot.OfflineValidUntil <= snapshot.IssuedAt ||
            snapshot.SubscriptionEndsAt < snapshot.OfflineValidUntil)
        {
            throw new CryptographicException("The offline lease validity window is invalid.");
        }

        return snapshot;
    }

    internal static byte[] Canonicalize(JsonElement element)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteCanonical(writer, element);
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonical(writer, item);
                }
                writer.WriteEndArray();
                break;

            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;

            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText(), skipInputValidation: true);
                break;

            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;

            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;

            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;

            default:
                throw new CryptographicException("The offline lease contains an unsupported JSON value.");
        }
    }

    private static string ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new CryptographicException($"Offline lease claim '{name}' is missing.");

    private static int ReadInt(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetInt32(out var result)
            ? result
            : throw new CryptographicException($"Offline lease claim '{name}' is invalid.");

    private static int? ReadOptionalInt(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.TryGetInt32(out var result) && result > 0
            ? result
            : throw new CryptographicException($"Offline lease claim '{name}' is invalid.");
    }

    private static DateTimeOffset ReadDate(JsonElement root, string name) =>
        DateTimeOffset.TryParse(ReadString(root, name), out var result)
            ? result
            : throw new CryptographicException($"Offline lease claim '{name}' is invalid.");

    private static byte[] DecodeBase64Url(string value)
    {
        value = value.Replace('-', '+').Replace('_', '/');
        value += (value.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            _ => string.Empty,
        };

        return Convert.FromBase64String(value);
    }
}
