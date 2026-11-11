namespace TabTabGo.Core.Models;

/// <summary>
/// Country reference data.
/// </summary>
public class Country
{
    /// <summary>Country name in the requested language.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>ISO 3166-1 alpha-2 code.</summary>
    [JsonPropertyName("alpha_2")]
    public string Alpha2 { get; set; } = string.Empty;

    /// <summary>ISO 3166-1 alpha-3 code.</summary>
    [JsonPropertyName("alpha_3")]
    public string Alpha3 { get; set; } = string.Empty;

    /// <summary>ISO 3166-1 numeric code.</summary>
    [JsonPropertyName("numeric")]
    public string Number { get; set; } = string.Empty;

    /// <summary>Official name.</summary>
    [JsonPropertyName("official_name")]
    public string? OfficialName { get; set; }

    /// <summary>International dialling code.</summary>
    [JsonPropertyName("phone_code")]
    public string? PhoneCode { get; set; }
}
