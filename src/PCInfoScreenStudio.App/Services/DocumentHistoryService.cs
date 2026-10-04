using System.Text.Json;
using System.Text.Json.Serialization;
using PCInfoScreenStudio.Models;

namespace PCInfoScreenStudio.Services;

internal sealed class DocumentHistoryService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string Capture(ThemeDocument document)
        => JsonSerializer.Serialize(document, Options);

    public ThemeDocument Restore(string snapshot)
        => JsonSerializer.Deserialize<ThemeDocument>(snapshot, Options)
           ?? throw new InvalidDataException("The editor history snapshot could not be restored.");
}
