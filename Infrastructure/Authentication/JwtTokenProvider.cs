using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Infrastructure.Authentication;

public interface IJwtTokenProvider
{
    string Generate(int userId, string mobile, string fullName, string role, DateTime expiresAt);
}

public sealed class JwtTokenProvider(IOptions<JwtOptions> options) : IJwtTokenProvider
{
    public string Generate(int userId, string mobile, string fullName, string role, DateTime expiresAt)
    {
        JwtOptions settings = options.Value;
        List<Claim> claims =
        [
            new("UserId", userId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(ClaimTypes.NameIdentifier, userId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(ClaimTypes.MobilePhone, mobile),
            new(ClaimTypes.Name, fullName),
            new(ClaimTypes.Role, role),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        ];
        claims.AddRange(TicketPermissions.ForRole(role)
            .Select(permission => new Claim(TicketPermissions.ClaimType, permission)));
        var token = new JwtSecurityToken(
            issuer: settings.ValidIssuer,
            audience: settings.ValidAudience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAt,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Secret)),
                SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
