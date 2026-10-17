using Database.Model.Tickets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Database.Configuration.Tickets;

public sealed class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> builder)
    {
        builder.ToTable("ChatMessages", "Ticketing");
        builder.HasIndex(message => new { message.TicketId, message.ClientMessageId }).IsUnique();
        builder.Property(message => message.Version).IsConcurrencyToken().HasDefaultValue(1L);

        builder
            .Property(reply => reply.Body)
            .HasMaxLength(4000);

        builder
            .Property(reply => reply.CreatedAt)
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.HasIndex(reply => new { reply.TicketId, reply.CreatedAt });

        builder
            .HasOne(reply => reply.Ticket)
            .WithMany(ticket => ticket.Messages)
            .HasForeignKey(reply => reply.TicketId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne(reply => reply.Author)
            .WithMany(user => user.Messages)
            .HasForeignKey(reply => reply.AuthorUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
