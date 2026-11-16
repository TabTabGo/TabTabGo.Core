using Nj = Newtonsoft.Json;

namespace TabTabGo.Core.Models;

/// <summary>
/// General response for all REST requests
/// </summary>
public class GeneralResponse
{
    /// <summary>Creates a response.</summary>
    /// <param name="message">Response message</param>
    /// <param name="code">Optional system code</param>
    /// <param name="data">Optional payload, serialized as <c>data</c></param>
    public GeneralResponse(string message, string? code, dynamic? data = null)
    {
        Message = message;
        Code = code;
        Data = data;
    }

    /// <summary>
    /// Response message
    /// </summary>
    public string Message { get; set; }

    /// <summary>System code.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Code { get; set; }

    /// <summary>Payload.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public dynamic? Data { get; set; }

    /// <summary>Extra properties flattened into the response object.</summary>
    [JsonExtensionData]
    [Nj.JsonExtensionData]
    public IDictionary<string, object?> Extra { get; set; } = new Dictionary<string, object?>();
}
