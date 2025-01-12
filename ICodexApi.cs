using Refit;

namespace OpenTelemetryPoc;

public interface ICodexApi
{
    [Get("/Thema/{id}")]
    Task<ThemaDto> GetThema(int id);
}

public record ThemaDto(int Id, string Omschrijving);
