namespace TabTabGo.Core.Models;

/// <summary>
/// General response for all REST requests
/// </summary>
public class GeneralResponse(string message, string? code, dynamic? data = null)
{
    /// <summary>
    /// Response message
    /// </summary>
    public string Message { get; set; } = message;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Code { get; set; } = code;

    [JsonExtensionData]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public dynamic? Data { get; set; } = data;
}