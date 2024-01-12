using System.Text.Json.Serialization;

namespace TabTabGo.Core.Models
{
    public class Country
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("alpha_2")]
        public string Alpha2 { get; set; }

        [JsonPropertyName("alpha_3")]
        public string Alpha3 { get; set; }

        [JsonPropertyName("numeric")]
        public string Number { get; set; }

        [JsonPropertyName("official_name")]
        public string? OfficialName { get; set; }

        [JsonPropertyName("phone_extension")]
        public string? PhoneExtension { get; set; }
    }
}