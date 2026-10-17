using Database.Model.Tickets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Database.Configuration.Tickets
{
    public sealed class MessageAttachmentConfiguration : IEntityTypeConfiguration<MessageAttachment>
    {
        public void Configure(EntityTypeBuilder<MessageAttachment> builder)
        {
            builder.ToTable("MessageAttachments", "Ticketing");
            builder.Property(attachment => attachment.FileName).HasMaxLength(200).IsRequired();
            builder.Property(attachment => attachment.ContentType).HasMaxLength(100).IsRequired();
            builder.Property(attachment => attachment.StorageKey).HasMaxLength(100);
            builder.HasOne(attachment => attachment.Message).WithMany(message => message.Attachments)
                .HasForeignKey(attachment => attachment.MessageId).OnDelete(DeleteBehavior.Cascade);
        }
    }
}
