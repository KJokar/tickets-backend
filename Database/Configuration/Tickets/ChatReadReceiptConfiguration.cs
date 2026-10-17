using Database.Model.Identity;
using Database.Model.Tickets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Database.Configuration.Tickets;

public sealed class ChatReadReceiptConfiguration : IEntityTypeConfiguration<ChatReadReceipt>
{
    public void Configure(EntityTypeBuilder<ChatReadReceipt> builder)
    {
        builder.ToTable("ChatReadReceipts", "Ticketing");
        builder.HasKey(receipt => new { receipt.TicketId, receipt.UserId });
        builder.HasOne<Ticket>().WithMany().HasForeignKey(receipt => receipt.TicketId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(receipt => receipt.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
