using Asp.Versioning;
using Infrastructure.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Service.Identity;
using ViewModels.Common;
using ViewModels.Identity;

namespace TicketingApi.Areas.Identity.Controllers
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v1/auth")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed class AuthController(IAuthService authService, IPasswordResetService passwordResetService) : ControllerBase
    {
        [AllowAnonymous, HttpPost("forgot-password/request")]
        [EnableRateLimiting(AuthPolicies.AuthenticationRateLimit)]
        public async Task<ActionResult<ApiResultDto<RequestPasswordResetResponse>>> RequestPasswordResetAsync(RequestPasswordResetRequest request, CancellationToken cancellationToken)
        {
            var result = await passwordResetService.RequestPasswordResetAsync(request, cancellationToken);
            return StatusCode(result.StatusCode, result);
        }
        [AllowAnonymous, HttpPost("forgot-password/verify")]
        [EnableRateLimiting(AuthPolicies.AuthenticationRateLimit)]
        public async Task<ActionResult<ApiResultDto<VerifyPasswordResetResponse>>> VerifyPasswordResetAsync(VerifyPasswordResetRequest request, CancellationToken cancellationToken)
        {
            var result = await passwordResetService.VerifyPasswordResetAsync(request, cancellationToken);
            return StatusCode(result.StatusCode, result);
        }
        [AllowAnonymous, HttpPost("forgot-password/reset")]
        [EnableRateLimiting(AuthPolicies.AuthenticationRateLimit)]
        public async Task<ActionResult<ApiResultDto<ResetPasswordResponse>>> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken)
        {
            var result = await passwordResetService.ResetPasswordAsync(request, cancellationToken);
            return StatusCode(result.StatusCode, result);
        }
        [AllowAnonymous]
        [HttpGet("roles")]
        [ProducesResponseType(typeof(ApiResultDto<IReadOnlyList<RoleOptionDto>>), StatusCodes.Status200OK, "application/json")]
        public ActionResult<ApiResultDto<IReadOnlyList<RoleOptionDto>>> Roles()
        {
            ApiResultDto<IReadOnlyList<RoleOptionDto>> result = authService.GetRoles();

            return StatusCode(result.StatusCode, result);
        }

        [AllowAnonymous]
        [HttpPost("register")]
        [EnableRateLimiting(AuthPolicies.AuthenticationRateLimit)]
        [ProducesResponseType(typeof(ApiResultDto<AuthResponse>), StatusCodes.Status201Created, "application/json")]
        [ProducesResponseType(typeof(ApiResultDto<AuthResponse>), StatusCodes.Status400BadRequest, "application/json")]
        [ProducesResponseType(typeof(ApiResultDto<AuthResponse>), StatusCodes.Status409Conflict, "application/json")]
        [ProducesResponseType(typeof(ApiResultDto<object>), StatusCodes.Status429TooManyRequests, "application/json")]
        public async Task<ActionResult<ApiResultDto<AuthResponse>>> RegisterAsync(
            [FromBody] RegisterRequest request,
            CancellationToken cancellationToken)
        {
            ApiResultDto<AuthResponse> result = await authService.RegisterAsync(
                request,
                cancellationToken);

            return StatusCode(result.StatusCode, result);
        }

        [AllowAnonymous]
        [HttpPost("login")]
        [EnableRateLimiting(AuthPolicies.AuthenticationRateLimit)]
        [ProducesResponseType(typeof(ApiResultDto<AuthResponse>), StatusCodes.Status200OK, "application/json")]
        [ProducesResponseType(typeof(ApiResultDto<AuthResponse>), StatusCodes.Status400BadRequest, "application/json")]
        [ProducesResponseType(typeof(ApiResultDto<AuthResponse>), StatusCodes.Status401Unauthorized, "application/json")]
        [ProducesResponseType(typeof(ApiResultDto<object>), StatusCodes.Status429TooManyRequests, "application/json")]
        public async Task<ActionResult<ApiResultDto<AuthResponse>>> LoginAsync(
            [FromBody] LoginRequest request,
            CancellationToken cancellationToken)
        {
            ApiResultDto<AuthResponse> result = await authService.LoginAsync(
                request,
                cancellationToken);

            return StatusCode(result.StatusCode, result);
        }

        [AllowAnonymous]
        [HttpPost("refresh")]
        [EnableRateLimiting(AuthPolicies.AuthenticationRateLimit)]
        [ProducesResponseType(typeof(ApiResultDto<AuthResponse>), StatusCodes.Status200OK, "application/json")]
        [ProducesResponseType(typeof(ApiResultDto<AuthResponse>), StatusCodes.Status400BadRequest, "application/json")]
        [ProducesResponseType(typeof(ApiResultDto<AuthResponse>), StatusCodes.Status401Unauthorized, "application/json")]
        [ProducesResponseType(typeof(ApiResultDto<object>), StatusCodes.Status429TooManyRequests, "application/json")]
        public async Task<ActionResult<ApiResultDto<AuthResponse>>> RefreshAsync(
            [FromBody] RefreshTokenRequest request,
            CancellationToken cancellationToken)
        {
            ApiResultDto<AuthResponse> result = await authService.RefreshAsync(request, cancellationToken);

            return StatusCode(result.StatusCode, result);
        }
    }
}
