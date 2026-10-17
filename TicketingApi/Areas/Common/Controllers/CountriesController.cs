using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Service.Common;
using ViewModels.Common;
using ViewModels.Identity;

namespace TicketingApi.Areas.Common.Controllers
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("Common/v1/[controller]")]
    public class CountriesController(ICountriesService countriesService)
        : ControllerBase
    {
        [HttpGet("List")]
        [ProducesResponseType(
            typeof(ApiResultDto<IReadOnlyList<CountryLookupDto>>),
            StatusCodes.Status200OK,
            "application/json")]
        public async Task<ActionResult<ApiResultDto<IReadOnlyList<CountryLookupDto>>>>
            ListAsync(
                [FromQuery] string? search,
                CancellationToken cancellationToken)
        {
            ApiResultDto<IReadOnlyList<CountryLookupDto>> result =
                await countriesService.ListAsync(search, cancellationToken);

            return StatusCode(result.StatusCode, result);
        }

        [HttpGet("Select/{id:int}")]
        [ProducesResponseType(
            typeof(ApiResultDto<CountryLookupDto>),
            StatusCodes.Status200OK,
            "application/json")]
        public async Task<ActionResult<ApiResultDto<CountryLookupDto>>> SelectAsync(
            int id,
            CancellationToken cancellationToken)
        {
            ApiResultDto<CountryLookupDto> result =
                await countriesService.SelectAsync(id, cancellationToken);

            return StatusCode(result.StatusCode, result);
        }
    }
}
