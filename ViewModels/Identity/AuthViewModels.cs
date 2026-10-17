using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Database.Enums.Identity;

namespace ViewModels.Identity
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class RegisterRequest
    {
        [Required, StringLength(150, MinimumLength = 2)]
        public string FullName { get; set; } = null!;

        [Range(1, int.MaxValue)]
        public int CountryId { get; set; }
        [Required, StringLength(20, MinimumLength = 8)]
        public string Mobile { get; set; } = null!;

        [Required, StringLength(128)]
        public string Password { get; set; } = null!;

        [Required, Compare(nameof(Password)), StringLength(128)]
        public string ConfirmPassword { get; set; } = null!;

        [Required, EnumDataType(typeof(UserRole))]
        public UserRole? Role { get; set; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class LoginRequest
    {
        [Range(1, int.MaxValue)]
        public int CountryId { get; set; }
        [Required, StringLength(20, MinimumLength = 8)]
        public string Mobile { get; set; } = null!;

        [Required, StringLength(128)]
        public string Password { get; set; } = null!;
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class RefreshTokenRequest
    {
        [Required, StringLength(128)]
        public string RefreshToken { get; set; } = null!;
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class EditProfileRequest
    {
        [Required, StringLength(150, MinimumLength = 2)]
        public string FullName { get; set; } = null!;
    }

    public sealed record AuthResponse(
        AuthUserDto User,
        DateTime ExpiresAt,
        string Token,
        string RefreshToken,
        DateTime RefreshTokenExpiresAt);

    public sealed record RoleOptionDto(int Id, UserRole Value, string Label);

    public sealed record AuthUserDto(
        int Id,
        string FullName,
        string Mobile,
        int CountryId,
        UserRole Role,
        IReadOnlyList<string> Permissions,
        string? AvatarUrl = null);

}
