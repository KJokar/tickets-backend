using Database.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Database.Configuration.Common
{
    public class CountriesConfiguration : IEntityTypeConfiguration<Countries>
    {
        public void Configure(EntityTypeBuilder<Countries> builder)
        {
            builder.ToTable(nameof(Countries), "Common");

            builder
                .HasIndex(country => country.IsoCode)
                .IsUnique();

            builder.HasIndex(country => country.PhoneCode);
            builder.HasData(Database.Seeds.CountrySeed.All);
        }
    }
}
