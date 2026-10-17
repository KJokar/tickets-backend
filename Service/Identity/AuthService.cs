using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Database.Context;
using Database.Enums.Identity;
using Database.Model.Identity;
using Infrastructure.Authentication;
using Infrastructure.Helper;
using Repository.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Repository.Identity;
using ViewModels.Common;
using ViewModels.Identity;

namespace Service.Identity
{
    public interface IAuthService
    {
        ApiResultDto<IReadOnlyList<RoleOptionDto>> GetRoles();
        Task<ApiResultDto<AuthResponse>> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);
        Task<ApiResultDto<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
        Task<ApiResultDto<AuthResponse>> RefreshAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default);
    }

    public sealed class AuthService(
        IUsersRepository usersRepository,
        IRefreshTokensRepository refreshTokensRepository,
        IDatabaseUnitOfWork database,
        IPasswordHasher passwordHasher,
        IJwtTokenProvider jwtTokenProvider,
        IOptions<JwtOptions> jwtOptions, ICountriesRepository countriesRepository) : IAuthService
    {
        private static readonly string DummyHash = new Pbkdf2PasswordHasher().Hash("Timing-Only-Dummy-Password-123!");

        public ApiResultDto<IReadOnlyList<RoleOptionDto>> GetRoles()
        {
            IReadOnlyList<RoleOptionDto> roles = Enum.GetValues<UserRole>()
                .Select(role => new RoleOptionDto(
                    (int)role,
                    role,
                    typeof(UserRole).GetField(role.ToString())?
                        .GetCustomAttribute<DisplayAttribute>()?.GetName() ?? role.ToString()))
                .ToList();

            return ApiResultDto<IReadOnlyList<RoleOptionDto>>.Success(roles);
        }

        public async Task<ApiResultDto<AuthResponse>> RegisterAsync(
            RegisterRequest request,
            CancellationToken cancellationToken = default)
        {
            string? error = Validate(request);
            if (error != null)
            {
                return ApiResultDto<AuthResponse>.Failure(error);
            }

            if (request.FullName.Trim().Length < 2)
            {
                return ApiResultDto<AuthResponse>.Failure(
                    "Full name must contain at least two non-padding characters.");
            }

            var country = await countriesRepository.GetByIdAsync(request.CountryId, cancellationToken);
            string? mobile = country == null ? null : PhoneNumberHelper.Normalize(country.PhoneCode, request.Mobile);
            if (mobile == null || mobile.Length is < 8 or > 16) return ApiResultDto<AuthResponse>.Failure("شماره موبایل یا کشور معتبر نیست.");
            if (await usersRepository.MobileExistsAsync(mobile, cancellationToken))
            {
                return Duplicate();
            }

            User user = new()
            {
                Mobile = mobile,
                CountryId = country!.Id,
                FullName = request.FullName.Trim(),
                PasswordHash = passwordHasher.Hash(request.Password),
                Role = request.Role!.Value,
                Status = UserStatus.Active
            };
            usersRepository.Add(user);

            try
            {
                return await IssueAsync(user, HttpStatusCode.Created, cancellationToken);
            }
            catch (DbUpdateException exception) when (IsDuplicateMobile(exception))
            {
                return Duplicate();
            }
        }

        public async Task<ApiResultDto<AuthResponse>> LoginAsync(
            LoginRequest request,
            CancellationToken cancellationToken = default)
        {
            string? error = Validate(request);
            if (error != null)
            {
                return ApiResultDto<AuthResponse>.Failure(error);
            }

            var country = await countriesRepository.GetByIdAsync(request.CountryId, cancellationToken);
            string? mobile = country == null ? null : PhoneNumberHelper.Normalize(country.PhoneCode, request.Mobile);
            if (mobile == null || mobile.Length is < 8 or > 16) return ApiResultDto<AuthResponse>.Failure("شماره موبایل یا کشور معتبر نیست.");
            User? user = await usersRepository.GetByMobileAsync(mobile, cancellationToken);
            bool passwordValid = passwordHasher.Verify(user?.PasswordHash ?? DummyHash, request.Password);
            if (user == null || !passwordValid || user.Status != UserStatus.Active || !Enum.IsDefined(user.Role))
            {
                return ApiResultDto<AuthResponse>.Failure(
                    "شماره موبایل یا رمز عبور صحیح نیست.",
                    (int)HttpStatusCode.Unauthorized);
            }

            if (passwordHasher.NeedsRehash(user.PasswordHash, request.Password))
            {
                user.PasswordHash = passwordHasher.Hash(request.Password);
                usersRepository.Modify(user);
            }

            return await IssueAsync(user, HttpStatusCode.OK, cancellationToken);
        }

        public async Task<ApiResultDto<AuthResponse>> RefreshAsync(
            RefreshTokenRequest request,
            CancellationToken cancellationToken = default)
        {
            string? error = Validate(request);
            if (error != null)
            {
                return ApiResultDto<AuthResponse>.Failure(error);
            }

            RefreshToken? token = await refreshTokensRepository.GetByHashAsync(
                HashToken(request.RefreshToken), cancellationToken);
            if (token == null || token.UsedAt != null || token.ExpiresAt <= DateTime.UtcNow ||
                token.User.Status != UserStatus.Active || !Enum.IsDefined(token.User.Role))
            {
                return InvalidRefreshToken();
            }

            token.UsedAt = DateTime.UtcNow;
            refreshTokensRepository.Modify(token);
            try
            {
                // Consumption and issuance are committed together by SaveChanges.
                return await IssueAsync(token.User, HttpStatusCode.OK, cancellationToken,
                    "Tokens refreshed successfully.");
            }
            catch (DbUpdateConcurrencyException)
            {
                return InvalidRefreshToken();
            }
        }

        private async Task<ApiResultDto<AuthResponse>> IssueAsync(
            User user,
            HttpStatusCode statusCode,
            CancellationToken cancellationToken,
            string? message = null)
        {
            string refreshToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(64));
            DateTime refreshTokenExpiresAt = DateTime.UtcNow.AddDays(jwtOptions.Value.RefreshTokenExpirationDays);
            refreshTokensRepository.Add(new RefreshToken
            {
                User = user,
                TokenHash = HashToken(refreshToken),
                ExpiresAt = refreshTokenExpiresAt
            });
            await database.SaveChangesAsync(cancellationToken);
            DateTime expiresAt = DateTime.UtcNow.AddMinutes(jwtOptions.Value.ExpirationMinutes);
            string token = jwtTokenProvider.Generate(
                user.Id,
                user.Mobile,
                user.FullName,
                user.Role.ToString(),
                expiresAt);

            return ApiResultDto<AuthResponse>.Success(
                new AuthResponse(
                    new AuthUserDto(
                        user.Id,
                        user.FullName,
                        user.Mobile,
                        user.CountryId,
                        user.Role,
                        TicketPermissions.ForRole(user.Role.ToString()), user.AvatarUrl),
                    expiresAt,
                    token,
                    refreshToken,
                    refreshTokenExpiresAt),
                message ?? (statusCode == HttpStatusCode.Created
                    ? "Registration completed successfully."
                    : "Login completed successfully."),
                (int)statusCode);
        }

        private static string HashToken(string token)
        {
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        }

        private static ApiResultDto<AuthResponse> InvalidRefreshToken()
        {
            return ApiResultDto<AuthResponse>.Failure(
                "Refresh token is invalid or expired.", (int)HttpStatusCode.Unauthorized);
        }

        private static ApiResultDto<AuthResponse> Duplicate()
        {
            return ApiResultDto<AuthResponse>.Failure(
                "این شماره موبایل قبلاً ثبت شده است.",
                (int)HttpStatusCode.Conflict);
        }

        private static bool IsDuplicateMobile(DbUpdateException exception)
        {
            return exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "IX_Users_Mobile"
            };
        }

        private static string? Validate(object request)
        {
            List<ValidationResult> errors = [];
            bool valid = Validator.TryValidateObject(
                request,
                new ValidationContext(request),
                errors,
                validateAllProperties: true);

            return valid ? null : errors[0].ErrorMessage;
        }
    }
}
