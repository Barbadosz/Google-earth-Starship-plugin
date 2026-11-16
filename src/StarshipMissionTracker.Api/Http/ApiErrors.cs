using System.Text.Json;
using StarshipMissionTracker.Api.Dtos;

namespace StarshipMissionTracker.Api.Http;

/// <summary>JSON error bodies <c>{"error":"...","reason":"..."}</c> (the control page shows <c>reason</c>).</summary>
public static class ApiErrors
{
    /// <summary>Serializer settings of every JSON response (camelCase, independent of the host's JsonOptions).</summary>
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IResult Result(int statusCode, string error, string reason) =>
        Results.Json(new ErrorDto(error, reason), JsonOptions, statusCode: statusCode);

    public static IResult BadRequest(string reason) => Result(StatusCodes.Status400BadRequest, "bad-request", reason);

    public static IResult NotFound(string reason) => Result(StatusCodes.Status404NotFound, "not-found", reason);

    public static IResult Conflict(string reason) => Result(StatusCodes.Status409Conflict, "conflict", reason);

    public static IResult Ok<T>(T value) => Results.Json(value, JsonOptions);
}
