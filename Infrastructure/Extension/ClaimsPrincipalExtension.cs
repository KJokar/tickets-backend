using System.Security.Claims;

namespace Infrastructure.Extension;

public static class ClaimsPrincipalExtension
{
    public static int? GetUserId(this ClaimsPrincipal user)
    {
        string? value = user.FindFirst("UserId")?.Value ??
            user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return int.TryParse(value, out int userId) ? userId : null;
    }

    public static string? GetUserRole(this ClaimsPrincipal user)
    {
        return user.FindFirst(ClaimTypes.Role)?.Value;
    }
}
