namespace ViewModels.Tickets
{
    public static class TicketMediaLimits
    {
        public const int MaxFiles = 5;
        public const int MaxTotalBytes = 20 * 1024 * 1024;
        public const int MaxRequestBytes = MaxTotalBytes + 64 * 1024;
    }

    public sealed record UploadedTicketMedia(string FileName, byte[] Content);
    public sealed record MessageAttachmentDto(Guid Id, string FileName, string ContentType, int Size, string? StorageKey = null);
    public sealed record MessageAttachmentContentDto(string FileName, string ContentType, byte[]? Content, string? StorageKey = null);
    public sealed record MediaFileReference(string Receipt);
    public sealed record MediaUploadRequest(string Category, int? TicketId = null);
    public sealed record MediaUploadDto(string UploadUrl, string Token);
    public sealed record MediaAccessDto(string Url, DateTime ExpiresAt);
    public sealed record MediaAuthorizeRequest(string Token);
}
