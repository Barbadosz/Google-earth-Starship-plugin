using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;

namespace StarshipMissionTracker.Api.Http;

/// <summary>
/// Strict reading of small JSON request bodies: must be one JSON object, at most <see cref="MaxBytes"/>, no duplicate
/// property names. Property names are case-sensitive (camelCase); unknown properties are ignored. Values are checked by
/// the caller (e.g. <see cref="TryGetNumber"/>), so a wrong type gives a clear 400 instead of a binding failure.
/// </summary>
public static class JsonBody
{
    public const int MaxBytes = 16 * 1024;

    private static readonly JsonDocumentOptions DocumentOptions = new() { MaxDepth = 8, AllowTrailingCommas = false };

    /// <summary>Returns the parsed object, or an error result (400 / 413).</summary>
    public static async Task<(JsonElement Root, IResult? Error)> ReadObjectAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.ContentLength > MaxBytes)
        {
            return (default, ApiErrors.Result(StatusCodes.Status413PayloadTooLarge, "payload-too-large", $"Body larger than {MaxBytes} bytes."));
        }

        var sizeFeature = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is { IsReadOnly: false })
        {
            sizeFeature.MaxRequestBodySize = MaxBytes;
        }

        try
        {
            using var buffer = new MemoryStream();
            await CopyLimitedAsync(request.Body, buffer, cancellationToken).ConfigureAwait(false);
            if (buffer.Length == 0)
            {
                return (default, ApiErrors.BadRequest("A JSON object body is required."));
            }

            using var document = JsonDocument.Parse(buffer.ToArray(), DocumentOptions);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (default, ApiErrors.BadRequest("The body must be a JSON object."));
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    return (default, ApiErrors.BadRequest($"Duplicate property '{property.Name}'."));
                }
            }

            return (root.Clone(), null);
        }
        catch (JsonException)
        {
            return (default, ApiErrors.BadRequest("The body is not valid JSON."));
        }
        catch (BodyTooLargeException)
        {
            return (default, ApiErrors.Result(StatusCodes.Status413PayloadTooLarge, "payload-too-large", $"Body larger than {MaxBytes} bytes."));
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return (default, ApiErrors.Result(StatusCodes.Status413PayloadTooLarge, "payload-too-large", $"Body larger than {MaxBytes} bytes."));
        }
    }

    /// <summary>
    /// Reads a required/optional JSON number. Missing -> (null, null) when optional; wrong type, non-finite or missing
    /// required value -> error reason.
    /// </summary>
    public static (double? Value, string? Error) TryGetNumber(JsonElement root, string name, bool required)
    {
        if (!root.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return required ? (null, $"'{name}' is required.") : (null, null);
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetDouble(out var value) || !double.IsFinite(value))
        {
            return (null, $"'{name}' must be a finite JSON number.");
        }

        return (value, null);
    }

    /// <summary>Reads a JSON string; missing -> (null, null) when optional; wrong type or missing required -> error.</summary>
    public static (string? Value, string? Error) TryGetString(JsonElement root, string name, bool required)
    {
        if (!root.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return required ? (null, $"'{name}' is required.") : (null, null);
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            return (null, $"'{name}' must be a JSON string.");
        }

        return (element.GetString(), null);
    }

    private static async Task CopyLimitedAsync(Stream source, MemoryStream target, CancellationToken cancellationToken)
    {
        var chunk = new byte[4096];
        int read;
        while ((read = await source.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (target.Length + read > MaxBytes)
            {
                throw new BodyTooLargeException();
            }

            target.Write(chunk, 0, read);
        }
    }

    private sealed class BodyTooLargeException : Exception;
}
