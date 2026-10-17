using Database.Context;
using Database.Model;
using Microsoft.EntityFrameworkCore;
using ViewModels.Identity;

namespace Repository.Common
{
    public interface ICountriesRepository
    {
        Task<IReadOnlyList<CountryLookupDto>> GetListAsync(
            string? search,
            CancellationToken cancellationToken = default);

        Task<CountryLookupDto?> GetSelectByIdAsync(
            int id,
            CancellationToken cancellationToken = default);

        Task<Countries?> GetByIdAsync(
            int id,
            CancellationToken cancellationToken = default);
    }

    public class CountriesRepository : ICountriesRepository
    {
        private readonly DatabaseContext _context;

        public CountriesRepository(DatabaseContext context)
        {
            _context = context;
        }

        public async Task<IReadOnlyList<CountryLookupDto>> GetListAsync(
            string? search,
            CancellationToken cancellationToken = default)
        {
            IQueryable<Countries> query = _context.Countries.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(search))
            {
                string term = search.Trim().ToLower();

                query = query.Where(country =>
                    country.Name.ToLower().Contains(term) ||
                    country.IsoCode.ToLower().Contains(term) ||
                    country.PhoneCode.Contains(term));
            }

            return await query
                .OrderBy(country => country.Name)
                .Select(country => new CountryLookupDto
                {
                    Id = country.Id,
                    Name = country.Name,
                    IsoCode = country.IsoCode,
                    PhoneCode = country.PhoneCode,
                    Flag = country.Flag
                })
                .ToListAsync(cancellationToken);
        }

        public Task<CountryLookupDto?> GetSelectByIdAsync(
            int id,
            CancellationToken cancellationToken = default)
        {
            return _context.Countries
                .AsNoTracking()
                .Where(country => country.Id == id)
                .Select(country => new CountryLookupDto
                {
                    Id = country.Id,
                    Name = country.Name,
                    IsoCode = country.IsoCode,
                    PhoneCode = country.PhoneCode,
                    Flag = country.Flag
                })
                .SingleOrDefaultAsync(cancellationToken);
        }

        public Task<Countries?> GetByIdAsync(
            int id,
            CancellationToken cancellationToken = default)
        {
            return _context.Countries
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    country => country.Id == id,
                    cancellationToken);
        }
    }
}
