using Database.Context;
using Database.Enums.Identity;
using Database.Enums.Tickets;
using Database.Model.Tickets;
using Infrastructure.Authentication;
using Microsoft.Extensions.Configuration;
using Repository.Identity;
using Repository.Tickets;
using Ticketing.Media;
using ViewModels.Common;
using ViewModels.Identity;
using ViewModels.Tickets;

namespace Service.Media
{
    public interface IMediaService
    {
        Task<ApiResultDto<MediaUploadDto>> UploadGrantAsync(int userId, UserRole role, MediaUploadRequest request, CancellationToken cancellationToken);
        Task<(List<MessageAttachment> Files, string? Error)> ResolveAsync(int userId, int? ticketId, IReadOnlyList<MediaFileReference>? files, CancellationToken cancellationToken);
        Task<ApiResultDto<MediaAccessDto>> AccessAsync(int userId, UserRole role, int ticketId, int messageId, Guid fileId, CancellationToken cancellationToken);
        Task<bool> AuthorizeAsync(string token, CancellationToken cancellationToken);
        Task<ApiResultDto<AuthUserDto>> ProfileAsync(int userId, CancellationToken cancellationToken);
        Task<ApiResultDto<AuthUserDto>> EditProfileAsync(int userId, EditProfileRequest request, CancellationToken cancellationToken);
        Task<ApiResultDto<AuthUserDto>> SetAvatarAsync(int userId, MediaFileReference? reference, CancellationToken cancellationToken);
    }

    public sealed class MediaService(ITicketsRepository tickets, IUsersRepository users, IDatabaseUnitOfWork database,
        MediaTokens tokens, IConfiguration configuration) : IMediaService
    {
        private string Cdn => (configuration["Media:CdnBaseUrl"] ?? "http://localhost:5083").TrimEnd('/');
        public async Task<ApiResultDto<MediaUploadDto>> UploadGrantAsync(int userId, UserRole role, MediaUploadRequest request, CancellationToken cancellationToken)
        {
            if (request.Category is not ("UserAvatar" or "MessageAttachment")) return ApiResultDto<MediaUploadDto>.Failure("نوع آپلود معتبر نیست.", 400);
            if (request.Category == "MessageAttachment")
            {
                if (request.TicketId.HasValue)
                {
                    var ticket = await tickets.GetAsync(request.TicketId.Value, userId, role, cancellationToken);
                    if (ticket == null) return ApiResultDto<MediaUploadDto>.Failure("درخواست پیدا نشد.", 404);
                    if (ticket.Status == TicketStatus.Closed || role == UserRole.Agent && ticket.AssignedToId != userId)
                        return ApiResultDto<MediaUploadDto>.Failure("امکان ارسال فایل در این گفت‌وگو وجود ندارد.", 409);
                }
                else if (role != UserRole.Client) return ApiResultDto<MediaUploadDto>.Failure("ابتدا درخواست را بپذیرید.", 403);
            }
            string token = tokens.Issue(new("upload", userId, request.Category, DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeSeconds(), Guid.NewGuid(), request.TicketId, role.ToString()));
            return ApiResultDto<MediaUploadDto>.Success(new(Cdn + "/api/V1/Files/" + request.Category, token));
        }
        public async Task<(List<MessageAttachment> Files, string? Error)> ResolveAsync(int userId, int? ticketId, IReadOnlyList<MediaFileReference>? files, CancellationToken cancellationToken)
        {
            List<MessageAttachment> result = [];
            if (files == null || files.Count > 5) return (result, "حداکثر ۵ پیوست مجاز است.");
            foreach (var reference in files)
            {
                MediaGrant? file = tokens.Validate(reference?.Receipt, "receipt");
                if (file == null || file.UserId != userId || file.Category != "MessageAttachment" || file.Size <= 0 || file.Size > TicketMediaLimits.MaxTotalBytes ||
                    file.TicketId.HasValue && file.TicketId != ticketId || string.IsNullOrEmpty(file.FileName) || file.FileName.Length > 200 || string.IsNullOrEmpty(file.ContentType))
                    return ([], "مجوز فایل معتبر نیست یا منقضی شده است. فایل را دوباره ارسال کنید.");
                if (result.Any(item => item.Id == file.Id) || await tickets.FileIsAttachedAsync(file.Id, cancellationToken))
                    return ([], "این فایل قبلاً به یک پیام متصل شده است.");
                result.Add(new() { Id = file.Id, StorageKey = file.Id.ToString("N"), FileName = file.FileName, ContentType = file.ContentType, Size = file.Size });
            }
            return result.Sum(file => (long)file.Size) > TicketMediaLimits.MaxTotalBytes ? ([], "مجموع حجم پیوست‌ها باید حداکثر ۲۰ مگابایت باشد.") : (result, null);
        }
        public async Task<ApiResultDto<MediaAccessDto>> AccessAsync(int userId, UserRole role, int ticketId, int messageId, Guid fileId, CancellationToken cancellationToken)
        {
            var file = await tickets.GetAttachmentAsync(ticketId, messageId, fileId, userId, role, cancellationToken);
            if (file?.StorageKey == null) return ApiResultDto<MediaAccessDto>.Failure("پیوست پیدا نشد.", 404);
            DateTime expiry = DateTime.UtcNow.AddMinutes(5);
            string token = tokens.Issue(new("read", userId, "MessageAttachment", new DateTimeOffset(expiry).ToUnixTimeSeconds(), fileId, ticketId, role.ToString()));
            return ApiResultDto<MediaAccessDto>.Success(new(Cdn + $"/api/V1/Files/Content/{fileId}?token=" + Uri.EscapeDataString(token), expiry));
        }
        public async Task<bool> AuthorizeAsync(string token, CancellationToken cancellationToken)
        {
            MediaGrant? grant = tokens.Validate(token, "read");
            return grant != null && Enum.TryParse(grant.Role, out UserRole role) && await tickets.CanReadFileAsync(grant.Id, grant.UserId, role, cancellationToken);
        }
        public async Task<ApiResultDto<AuthUserDto>> ProfileAsync(int userId, CancellationToken cancellationToken)
        {
            var user = await users.GetByIdAsync(userId, cancellationToken);
            return user == null ? ApiResultDto<AuthUserDto>.Failure("کاربر پیدا نشد.", 404) : ApiResultDto<AuthUserDto>.Success(new(user.Id, user.FullName, user.Mobile, user.CountryId, user.Role, TicketPermissions.ForRole(user.Role.ToString()), user.AvatarUrl));
        }
        public async Task<ApiResultDto<AuthUserDto>> EditProfileAsync(int userId, EditProfileRequest request, CancellationToken cancellationToken)
        {
            string fullName = request.FullName.Trim();
            if (fullName.Length < 2 || fullName.Length > 150)
                return ApiResultDto<AuthUserDto>.Failure("نام باید بین ۲ تا ۱۵۰ کاراکتر باشد.", 400);
            var user = await users.GetByIdAsync(userId, cancellationToken);
            if (user == null) return ApiResultDto<AuthUserDto>.Failure("کاربر پیدا نشد.", 404);
            user.FullName = fullName;
            await database.SaveChangesAsync(cancellationToken);
            return await ProfileAsync(userId, cancellationToken);
        }
        public async Task<ApiResultDto<AuthUserDto>> SetAvatarAsync(int userId, MediaFileReference? reference, CancellationToken cancellationToken)
        {
            var user = await users.GetByIdAsync(userId, cancellationToken);
            if (user == null) return ApiResultDto<AuthUserDto>.Failure("کاربر پیدا نشد.", 404);
            var file = reference == null ? null : tokens.Validate(reference.Receipt, "receipt");
            if (reference != null && (file == null || file.UserId != userId || file.Category != "UserAvatar" || file.Url == null || !file.Url.StartsWith("/users/avatars/") || file.ContentType == null || !file.ContentType.StartsWith("image/")))
                return ApiResultDto<AuthUserDto>.Failure("فایل تصویر پروفایل معتبر نیست.", 400);
            user.AvatarUrl = file?.Url;
            await database.SaveChangesAsync(cancellationToken);
            return await ProfileAsync(userId, cancellationToken);
        }
    }
}
