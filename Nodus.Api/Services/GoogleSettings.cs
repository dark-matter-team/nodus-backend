namespace Nodus.Api.Services;

public class GoogleSettings
{
    public const string SectionName = "Google";

    public string ClientId { get; set; } = string.Empty;
    public List<string> AllowedClientIds { get; set; } = [];

    public IReadOnlyList<string> GetAllClientIds()
    {
        return AllowedClientIds
            .Prepend(ClientId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}