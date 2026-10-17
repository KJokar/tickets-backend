using System.Net;

namespace ViewModels.Common;

public record ApiResultDto<T>(
    string? Message,
    T? Data,
    int StatusCode,
    PaginationMeta? Pagination = null)
    where T : class
{
    public static ApiResultDto<T> Success(
        T? data,
        string message = "Success",
        int statusCode = (int)HttpStatusCode.OK,
        PaginationMeta? pagination = null)
    {
        return new ApiResultDto<T>(message, data, statusCode, pagination);
    }

    public static ApiResultDto<T> Failure(
        string message = "An error occurred",
        int statusCode = (int)HttpStatusCode.BadRequest)
    {
        return new ApiResultDto<T>(message, null, statusCode);
    }
}

public sealed record PaginationMeta(
    int Page,
    int PerPage,
    int TotalItems,
    int TotalPages);
