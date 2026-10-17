using System.Net;
using System.Security.Cryptography;
using Database.Context;
using Database.Model;
using Database.Model.Identity;
using Infrastructure.Authentication;
using Infrastructure.Helper;
using Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Repository.Common;
using Repository.Identity;
using ViewModels.Common;
using ViewModels.Identity;

namespace Service.Identity
{
    public interface IPasswordResetService
    {
        Task<ApiResultDto<RequestPasswordResetResponse>> RequestPasswordResetAsync(RequestPasswordResetRequest request, CancellationToken cancellationToken = default);
        Task<ApiResultDto<VerifyPasswordResetResponse>> VerifyPasswordResetAsync(VerifyPasswordResetRequest request, CancellationToken cancellationToken = default);
        Task<ApiResultDto<ResetPasswordResponse>> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default);
    }
    public sealed class PasswordResetService(IUsersRepository usersRepository, ICountriesRepository countriesRepository,
        IDatabaseUnitOfWork database, IPasswordHasher passwordHasher, ISmsSender smsSender, IRefreshTokensRepository refreshTokens) : IPasswordResetService
    {
        private const int PasswordResetCodeLifetimeMinutes = 5;
        private const int PasswordResetTokenLifetimeMinutes = 5;
        private const int MaximumPasswordResetAttempts = 5;
        public async Task<ApiResultDto<RequestPasswordResetResponse>>
            RequestPasswordResetAsync(
                RequestPasswordResetRequest request,
                CancellationToken cancellationToken = default)
        {
            Countries? country = await countriesRepository.GetByIdAsync(
                request.CountryId,
                cancellationToken);

            if (country == null)
            {
                return ApiResultDto<RequestPasswordResetResponse>.Failure(
                    "کشور انتخاب‌شده معتبر نیست.");
            }

            string? mobile = PhoneNumberHelper.Normalize(
                country.PhoneCode,
                request.Mobile);

            if (mobile == null || mobile.Length is < 8 or > 16)
            {
                return ApiResultDto<RequestPasswordResetResponse>.Failure(
                    "شماره موبایل معتبر نیست.");
            }

            User? user = await usersRepository.GetForPasswordResetAsync(
                mobile,
                cancellationToken);

            if (user == null)
            {
                return ApiResultDto<RequestPasswordResetResponse>.Failure(
                    "کاربری با این شماره موبایل پیدا نشد.",
                    (int)HttpStatusCode.NotFound);
            }

            string code = RandomNumberGenerator
                .GetInt32(100000, 1000000)
                .ToString();
            DateTime expiresAt = DateTime.UtcNow.AddMinutes(
                PasswordResetCodeLifetimeMinutes);

            ClearPasswordResetToken(user);
            user.PasswordResetCodeHash = passwordHasher.Hash(code);
            user.PasswordResetCodeExpiresAt = expiresAt;
            user.PasswordResetCodeAttempts = 0;
            if (!await SavePasswordResetAsync(user, cancellationToken))
                return ApiResultDto<RequestPasswordResetResponse>.Failure("درخواست بازیابی تغییر کرده است. دوباره تلاش کنید.");

            try
            {
                await smsSender.SendAsync(
                    mobile,
                    $"کد بازیابی رمز عبور شما: {code}",
                    cancellationToken);
            }
            catch (Exception exception) when (
                exception is not OperationCanceledException)
            {
                ClearPasswordResetCode(user);
                await SavePasswordResetAsync(user, cancellationToken);

                return ApiResultDto<RequestPasswordResetResponse>.Failure(
                    "ارسال پیامک با خطا مواجه شد. لطفاً دوباره تلاش کنید.",
                    (int)HttpStatusCode.BadGateway);
            }

            return ApiResultDto<RequestPasswordResetResponse>.Success(
                new RequestPasswordResetResponse
                {
                    ExpiresInSeconds = PasswordResetCodeLifetimeMinutes * 60
                },
                "کد یک‌بار مصرف ارسال شد.");
        }

        public async Task<ApiResultDto<VerifyPasswordResetResponse>> VerifyPasswordResetAsync(
            VerifyPasswordResetRequest request,
            CancellationToken cancellationToken = default)
        {
            Countries? country = await countriesRepository.GetByIdAsync(request.CountryId,cancellationToken);

            if (country == null)
                return ApiResultDto<VerifyPasswordResetResponse>.Failure("کشور انتخاب‌شده معتبر نیست.");


            string? mobile = PhoneNumberHelper.Normalize(country.PhoneCode,request.Mobile);

            if (mobile == null || mobile.Length is < 8 or > 16)
                return ApiResultDto<VerifyPasswordResetResponse>.Failure("شماره موبایل معتبر نیست.");


            User? user = await usersRepository.GetForPasswordResetAsync(mobile,cancellationToken);

            if (user == null)
                return ApiResultDto<VerifyPasswordResetResponse>.Failure("کاربری با این شماره موبایل پیدا نشد.",(int)HttpStatusCode.NotFound);


            if (user.PasswordResetCodeHash == null ||
                user.PasswordResetCodeExpiresAt == null ||
                user.PasswordResetCodeExpiresAt <= DateTime.UtcNow ||
                user.PasswordResetCodeAttempts >= MaximumPasswordResetAttempts)
            {
                ClearPasswordResetCode(user);
                await SavePasswordResetAsync(user, cancellationToken);

                return ApiResultDto<VerifyPasswordResetResponse>.Failure("کد یک‌بار مصرف نامعتبر یا منقضی شده است.");
            }

            if (!passwordHasher.Verify(user.PasswordResetCodeHash,request.Code))
            {
                user.PasswordResetCodeAttempts++;

                if (user.PasswordResetCodeAttempts >= MaximumPasswordResetAttempts)
                    ClearPasswordResetCode(user);

                await SavePasswordResetAsync(user, cancellationToken);

                return ApiResultDto<VerifyPasswordResetResponse>.Failure("کد یک‌بار مصرف نامعتبر یا منقضی شده است.");
            }

            string resetToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            ClearPasswordResetCode(user);
            user.PasswordResetTokenHash = passwordHasher.Hash(resetToken);
            user.PasswordResetTokenExpiresAt = DateTime.UtcNow.AddMinutes(
                PasswordResetTokenLifetimeMinutes);

            if (!await SavePasswordResetAsync(user, cancellationToken))
                return ApiResultDto<VerifyPasswordResetResponse>.Failure("کد یک‌بار مصرف نامعتبر یا منقضی شده است.");

            return ApiResultDto<VerifyPasswordResetResponse>.Success(
                new VerifyPasswordResetResponse
                {
                    ResetToken = resetToken,
                    ExpiresInSeconds = PasswordResetTokenLifetimeMinutes * 60
                },
                "شماره موبایل تأیید شد.");
        }

        public async Task<ApiResultDto<ResetPasswordResponse>> ResetPasswordAsync(
            ResetPasswordRequest request,
            CancellationToken cancellationToken = default)
        {
            Countries? country = await countriesRepository.GetByIdAsync(request.CountryId,cancellationToken);

            if (country == null)
                return ApiResultDto<ResetPasswordResponse>.Failure("کشور انتخاب‌شده معتبر نیست.");


            string? mobile = PhoneNumberHelper.Normalize(country.PhoneCode,request.Mobile);

            if (mobile == null || mobile.Length is < 8 or > 16)
                return ApiResultDto<ResetPasswordResponse>.Failure("شماره موبایل معتبر نیست.");


            User? user = await usersRepository.GetForPasswordResetAsync(mobile,cancellationToken);

            if (user == null)
                return ApiResultDto<ResetPasswordResponse>.Failure("کاربری با این شماره موبایل پیدا نشد.",(int)HttpStatusCode.NotFound);


            if (user.PasswordResetTokenHash == null ||
                user.PasswordResetTokenExpiresAt == null ||
                user.PasswordResetTokenExpiresAt <= DateTime.UtcNow ||
                !passwordHasher.Verify(user.PasswordResetTokenHash, request.ResetToken))
            {
                return ApiResultDto<ResetPasswordResponse>.Failure("مجوز تغییر رمز عبور نامعتبر یا منقضی شده است. دوباره کد تأیید دریافت کنید.");
            }

            user.PasswordHash = passwordHasher.Hash(request.Password);
            await refreshTokens.RevokeForUserAsync(user.Id, cancellationToken);
            ClearPasswordResetCode(user);
            ClearPasswordResetToken(user);
            if (!await SavePasswordResetAsync(user, cancellationToken))
                return ApiResultDto<ResetPasswordResponse>.Failure("مجوز تغییر رمز عبور قبلاً استفاده شده یا تغییر کرده است.");

            return ApiResultDto<ResetPasswordResponse>.Success(
                new ResetPasswordResponse
                {
                    PasswordChanged = true
                },
                "رمز عبور با موفقیت تغییر کرد.");
        }

        private async Task<bool> SavePasswordResetAsync(
            User user,
            CancellationToken cancellationToken)
        {
            usersRepository.Modify(user);
            try
            {
                await database.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateConcurrencyException)
            {
                return false;
            }
        }

        private static void ClearPasswordResetToken(User user)
        {
            user.PasswordResetTokenHash = null;
            user.PasswordResetTokenExpiresAt = null;
        }

        private static void ClearPasswordResetCode(User user)
        {
            user.PasswordResetCodeHash = null;
            user.PasswordResetCodeExpiresAt = null;
            user.PasswordResetCodeAttempts = 0;
        }
    }
}
