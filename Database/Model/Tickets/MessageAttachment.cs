namespace Database.Model.Tickets
{
    public sealed class MessageAttachment
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public int MessageId { get; set; }
        public string FileName { get; set; } = null!;
        public string ContentType { get; set; } = null!;
        public int Size { get; set; }
        public byte[]? Content { get; set; }
        public string? StorageKey { get; set; }
        public ChatMessage Message { get; set; } = null!;
    }
}
