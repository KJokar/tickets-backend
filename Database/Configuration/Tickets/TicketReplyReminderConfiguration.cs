using Database.Model.Tickets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Database.Configuration.Tickets;

public sealed class TicketReplyReminderConfiguration : IEntityTypeConfiguration<TicketReplyReminder>
{
    public void Configure(EntityTypeBuilder<TicketReplyReminder> builder)
    {
        builder.ToTable("TicketReplyReminders", "Ticketing");
        builder.HasKey(reminder => reminder.TicketId);
        builder.Property(reminder => reminder.Revision).IsConcurrencyToken();
        builder.HasIndex(reminder => reminder.NextReminderAt).HasFilter("\"NextReminderAt\" IS NOT NULL");
        builder.HasIndex(reminder => reminder.AgentId);
        builder.HasOne(reminder => reminder.Ticket).WithOne().HasForeignKey<TicketReplyReminder>(reminder => reminder.TicketId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(reminder => reminder.Agent).WithMany().HasForeignKey(reminder => reminder.AgentId).OnDelete(DeleteBehavior.Restrict);
    }
}
