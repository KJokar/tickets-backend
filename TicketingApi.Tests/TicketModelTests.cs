using Database.Context;
using Database.Model.Identity;
using Database.Model.Tickets;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace TicketingApi.Tests;

public sealed class TicketModelTests
{
    [Fact]
    public void Migrations_MatchCurrentModel()
    {
        using DatabaseContext context = CreateContext();
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public void User_Mobile_IsUnique()
    {
        using DatabaseContext context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(User))!;
        var index = Assert.Single(
            entity.GetIndexes(),
            item => item.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(User.Mobile)]));

        Assert.True(index.IsUnique);
        Assert.Equal(16, entity.FindProperty(nameof(User.Mobile))!.GetMaxLength());
    }

    [Fact]
    public void Ticket_HasExpectedLimitsAndRestrictedOwnerRelationship()
    {
        using DatabaseContext context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(Ticket))!;

        Assert.Equal(200, entity.FindProperty(nameof(Ticket.Title))!.GetMaxLength());
        Assert.Null(entity.FindProperty("Description"));
        Assert.Null(entity.FindProperty("Department"));
        Assert.True(entity.FindProperty(nameof(Ticket.Priority))!.IsNullable);
        Assert.True(entity.FindProperty(nameof(Ticket.AssignedToId))!.IsConcurrencyToken);

        var ownerForeignKey = Assert.Single(
            entity.GetForeignKeys(),
            item => item.Properties.Any(property => property.Name == nameof(Ticket.CreatedById)));
        Assert.Equal(DeleteBehavior.Restrict, ownerForeignKey.DeleteBehavior);
    }

    [Fact]
    public void ChatMessage_CascadesWithTicket_ButRestrictsAuthor()
    {
        using DatabaseContext context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(ChatMessage))!;

        var ticketForeignKey = Assert.Single(
            entity.GetForeignKeys(),
            item => item.PrincipalEntityType.ClrType == typeof(Ticket));
        var authorForeignKey = Assert.Single(
            entity.GetForeignKeys(),
            item => item.PrincipalEntityType.ClrType == typeof(User));

        Assert.Equal(DeleteBehavior.Cascade, ticketForeignKey.DeleteBehavior);
        Assert.Equal(DeleteBehavior.Restrict, authorForeignKey.DeleteBehavior);
        Assert.Equal(4000, entity.FindProperty(nameof(ChatMessage.Body))!.GetMaxLength());
    }

    private static DatabaseContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<DatabaseContext>()
            .UseNpgsql(
                "Host=localhost;Database=model_verification;Username=model;Password=model")
            .Options;

        return new DatabaseContext(options);
    }
}
