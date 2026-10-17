using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ViewModels.Identity
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class RequestPasswordResetRequest
    {
        [Range(1, int.MaxValue)]
        public int CountryId { get; set; }

        [Required(ErrorMessage = "شماره موبایل الزامی است.")]
        [StringLength(20, MinimumLength = 8)]
        public string Mobile { get; set; } = null!;
    }

    public sealed class RequestPasswordResetResponse
    {
        public int ExpiresInSeconds { get; set; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class VerifyPasswordResetRequest
    {
        [Range(1, int.MaxValue)]
        public int CountryId { get; set; }

        [Required(ErrorMessage = "شماره موبایل الزامی است.")]
        [StringLength(20, MinimumLength = 8)]
        public string Mobile { get; set; } = null!;

        [Required]
        [RegularExpression("^[0-9]{6}$", ErrorMessage = "کد یک‌بار مصرف باید ۶ رقم باشد.")]
        public string Code { get; set; } = null!;
    }

    public sealed class VerifyPasswordResetResponse
    {
        public string ResetToken { get; set; } = null!;

        public int ExpiresInSeconds { get; set; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class ResetPasswordRequest
    {
        [Range(1, int.MaxValue)]
        public int CountryId { get; set; }

        [Required(ErrorMessage = "شماره موبایل الزامی است.")]
        [StringLength(20, MinimumLength = 8)]
        public string Mobile { get; set; } = null!;

        [Required(ErrorMessage = "تأیید شماره موبایل الزامی است.")]
        [RegularExpression("^[A-Fa-f0-9]{64}$", ErrorMessage = "مجوز تغییر رمز عبور معتبر نیست.")]
        public string ResetToken { get; set; } = null!;

        [Required]
        [StringLength(128, MinimumLength = 1)]
        public string Password { get; set; } = null!;

        [Required(ErrorMessage = "تکرار رمز عبور الزامی است.")]
        [Compare(nameof(Password), ErrorMessage = "رمز عبور و تکرار آن یکسان نیستند.")]
        public string ConfirmPassword { get; set; } = null!;
    }

    public sealed class ResetPasswordResponse
    {
        public bool PasswordChanged { get; set; }
    }
}
