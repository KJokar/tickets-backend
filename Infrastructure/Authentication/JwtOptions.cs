using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Infrastructure.Authentication;

public sealed class JwtOptions
{
    [Required] public string Secret { get; set; } = "";
    [Required] public string ValidIssuer { get; set; } = "";
    [Required] public string ValidAudience { get; set; } = "";
    [Range(5, 43200)] public int ExpirationMinutes { get; set; } = 43200;
    [Range(31, 365)] public int RefreshTokenExpirationDays { get; set; } = 90;

    public bool HasStrongKey() => Encoding.UTF8.GetByteCount(Secret) >= 32;
}
