using Database.Enums.Tickets;
using Database.Model.Tickets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Database.Configuration.Tickets;

public sealed class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        builder.ToTable("Tickets", "Ticketing");

        builder
            .Property(ticket => ticket.Title)
            .HasMaxLength(200);

        builder
            .Property(ticket => ticket.Priority)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder
            .Property(ticket => ticket.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasSentinel((TicketStatus)0)
            .HasDefaultValue(TicketStatus.Open);

        builder
            .Property(ticket => ticket.CreatedAt)
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.HasIndex(ticket => ticket.CreatedById);
        builder.HasIndex(ticket => new { ticket.AssignedToId, ticket.Status });
        builder.Property(ticket => ticket.AssignedToId).IsConcurrencyToken();
        builder.Property(ticket => ticket.Status).IsConcurrencyToken();
        builder.Property(ticket => ticket.Revision).IsConcurrencyToken();
        builder.HasIndex(ticket => ticket.Status);
        builder.HasIndex(ticket => ticket.Priority);
        builder.HasIndex(ticket => ticket.CreatedAt);

        builder
            .HasOne(ticket => ticket.Owner)
            .WithMany(user => user.Tickets)
            .HasForeignKey(ticket => ticket.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(ticket => ticket.AssignedTo).WithMany()
            .HasForeignKey(ticket => ticket.AssignedToId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
