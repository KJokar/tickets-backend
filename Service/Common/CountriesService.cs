using System.Net;
using Repository.Common;
using ViewModels.Common;
using ViewModels.Identity;

namespace Service.Common
{
    public interface ICountriesService
    {
        Task<ApiResultDto<IReadOnlyList<CountryLookupDto>>> ListAsync(
            string? search,
            CancellationToken cancellationToken = default);

        Task<ApiResultDto<CountryLookupDto>> SelectAsync(
            int id,
            CancellationToken cancellationToken = default);
    }

    public sealed class CountriesService(ICountriesRepository countriesRepository)
        : ICountriesService
    {
        public async Task<ApiResultDto<IReadOnlyList<CountryLookupDto>>> ListAsync(
            string? search,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<CountryLookupDto> countries =
                await countriesRepository.GetListAsync(
                    search,
                    cancellationToken);

            return ApiResultDto<IReadOnlyList<CountryLookupDto>>.Success(countries);
        }

        public async Task<ApiResultDto<CountryLookupDto>> SelectAsync(
            int id,
            CancellationToken cancellationToken = default)
        {
            CountryLookupDto? country =
                await countriesRepository.GetSelectByIdAsync(
                    id,
                    cancellationToken);

            return country == null
                ? ApiResultDto<CountryLookupDto>.Failure(
                    "کشور یافت نشد.",
                    (int)HttpStatusCode.NotFound)
                : ApiResultDto<CountryLookupDto>.Success(country);
        }
    }
}
