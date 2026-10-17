using Infrastructure.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Service.Media;
using ViewModels.Identity;
using ViewModels.Tickets;

namespace TicketingApi.Areas.Tickets.Controllers
{
    [ApiController]
    [Authorize(Policy = AuthPolicies.TicketUser)]
    [Route("Tickets/v1/Media")]
    public sealed class MediaController(IMediaService media) : TicketingApiBaseController
    {
        [HttpPost("Uploads")]
        public async Task<IActionResult> Uploads(MediaUploadRequest request, CancellationToken cancellationToken)
        {
            var result = await media.UploadGrantAsync(CurrentUserId, CurrentUserRole, request, cancellationToken);
            return StatusCode(result.StatusCode, result);
        }
        [AllowAnonymous]
        [HttpPost("Authorize")]
        public async Task<IActionResult> AuthorizeFile(MediaAuthorizeRequest request, CancellationToken cancellationToken) =>
            await media.AuthorizeAsync(request.Token, cancellationToken) ? NoContent() : NotFound();
    }

    [ApiController]
    [Authorize(Policy = AuthPolicies.TicketUser)]
    [Route("Tickets/v1/Profile")]
    public sealed class ProfileController(IMediaService media) : TicketingApiBaseController
    {
        [HttpGet]
        public async Task<IActionResult> Profile(CancellationToken cancellationToken)
        {
            var result = await media.ProfileAsync(CurrentUserId, cancellationToken); return StatusCode(result.StatusCode, result);
        }
        [HttpPatch]
        public async Task<IActionResult> Edit(EditProfileRequest request, CancellationToken cancellationToken)
        {
            var result = await media.EditProfileAsync(CurrentUserId, request, cancellationToken);
            return StatusCode(result.StatusCode, result);
        }
        [HttpPut("Avatar")]
        public async Task<IActionResult> Avatar(MediaFileReference request, CancellationToken cancellationToken)
        {
            var result = await media.SetAvatarAsync(CurrentUserId, request, cancellationToken); return StatusCode(result.StatusCode, result);
        }
        [HttpDelete("Avatar")]
        public async Task<IActionResult> DeleteAvatar(CancellationToken cancellationToken)
        {
            var result = await media.SetAvatarAsync(CurrentUserId, null, cancellationToken); return StatusCode(result.StatusCode, result);
        }
    }
}
