using Database.Model.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Database.Configuration.Identity;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users", "Identity");

        builder
            .HasIndex(user => user.Mobile)
            .IsUnique();

        builder
            .Property(user => user.Mobile)
            .HasMaxLength(16);
        builder.HasOne(user => user.Country).WithMany().HasForeignKey(user => user.CountryId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(user => user.PasswordResetCodeHash).IsConcurrencyToken();
        builder.Property(user => user.PasswordResetCodeAttempts).IsConcurrencyToken();
        builder.Property(user => user.PasswordResetTokenHash).IsConcurrencyToken();

        builder
            .Property(user => user.FullName)
            .HasMaxLength(150);

        builder
            .Property(user => user.PasswordHash)
            .HasMaxLength(500);

        builder
            .Property(user => user.Role)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(user => user.Status)
            .HasConversion<string>().HasMaxLength(20)
            .HasSentinel((Database.Enums.Identity.UserStatus)0)
            .HasDefaultValue(Database.Enums.Identity.UserStatus.Active);

        builder
            .Property(user => user.CreatedAt)
            .HasDefaultValueSql("CURRENT_TIMESTAMP");
    }
}
