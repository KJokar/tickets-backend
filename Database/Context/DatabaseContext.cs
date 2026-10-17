using Database.Model.Identity;
using Database.Model.Tickets;
using Microsoft.EntityFrameworkCore;

namespace Database.Context;

public interface IDatabaseUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public class DatabaseContext : DbContext, IDatabaseUnitOfWork
{
    public DatabaseContext(DbContextOptions<DatabaseContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users { get; set; }
    public DbSet<Database.Model.Countries> Countries { get; set; }

    public DbSet<RefreshToken> RefreshTokens { get; set; }

    public DbSet<Ticket> Tickets { get; set; }

    public DbSet<ChatMessage> ChatMessages { get; set; }
    public DbSet<MessageAttachment> MessageAttachments { get; set; }
    public DbSet<ChatReadReceipt> ChatReadReceipts { get; set; }
    public DbSet<TicketReplyReminder> TicketReplyReminders { get; set; }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<ChatMessage>())
            if (entry.State == EntityState.Modified)
                entry.Entity.Version = entry.Property(message => message.Version).OriginalValue + 1;
        foreach (var entry in ChangeTracker.Entries<Ticket>())
            if (entry.State == EntityState.Modified) entry.Entity.Revision = Guid.NewGuid();
        foreach (var entry in ChangeTracker.Entries<TicketReplyReminder>())
            if (entry.State == EntityState.Modified) entry.Entity.Revision = Guid.NewGuid();
        foreach (var entry in ChangeTracker.Entries<User>())
        {
            if (entry.State == EntityState.Modified)
                entry.Entity.UpdatedAt = DateTime.UtcNow;
        }
        return base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DatabaseContext).Assembly);
    }
}
