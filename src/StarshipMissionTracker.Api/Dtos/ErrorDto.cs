namespace StarshipMissionTracker.Api.Dtos;

/// <summary>Error body: a short machine code and a plain-language reason.</summary>
public sealed record ErrorDto(string Error, string Reason);
