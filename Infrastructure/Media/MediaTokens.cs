using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ticketing.Media
{
    // This small wire protocol is shared with the API by identical source copies.
    // API access tokens are never accepted by the CDN.
    public sealed record MediaGrant(string Kind, int UserId, string Category, long Expires,
        Guid Id, int? TicketId = null, string? Role = null, string? FileName = null,
        string? ContentType = null, int Size = 0, string? Url = null);

    public sealed class MediaTokens
    {
        private readonly byte[] key;
        public MediaTokens(string secret)
        {
            if (Encoding.UTF8.GetByteCount(secret) < 32) throw new InvalidOperationException("Configure Media:SigningKey with at least 32 bytes.");
            key = Encoding.UTF8.GetBytes(secret);
        }
        public string Issue(MediaGrant grant)
        {
            string payload = Encode(JsonSerializer.SerializeToUtf8Bytes(grant));
            return payload + "." + Encode(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload)));
        }
        public MediaGrant? Validate(string? token, string kind)
        {
            if (string.IsNullOrEmpty(token) || token.Length > 8192) return null;
            try
            {
                string[] parts = token.Split('.');
                if (parts.Length != 2 || !CryptographicOperations.FixedTimeEquals(Decode(parts[1]), HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(parts[0])))) return null;
                MediaGrant? grant = JsonSerializer.Deserialize<MediaGrant>(Decode(parts[0]));
                return grant is { UserId: > 0 } && grant.Kind == kind && grant.Id != Guid.Empty && grant.Expires > DateTimeOffset.UtcNow.ToUnixTimeSeconds() ? grant : null;
            }
            catch (Exception error) when (error is FormatException or JsonException) { return null; }
        }
        private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        private static byte[] Decode(string text)
        {
            string value = text.Replace('-', '+').Replace('_', '/');
            return Convert.FromBase64String(value.PadRight((value.Length + 3) / 4 * 4, '='));
        }
    }
}
