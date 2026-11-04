using System.Text.Json.Serialization;
using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using TabTabGo.Core.Exceptions;
using TabTabGo.Core.Services;
using Nj = Newtonsoft.Json;

namespace TabTabGo.Core.Api.Errors;

/// <summary>
/// The one error body of the Api package: <c>{ message, code, number, traceId }</c> plus validation <c>errors</c> and,
/// only when explicitly enabled, <c>details</c>. Never contains an exception object or a stack trace by default (D-7).
/// </summary>
internal sealed class ApiErrorBody
{
    /// <summary>Human readable message.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>System error code.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Nj.JsonProperty(NullValueHandling = Nj.NullValueHandling.Ignore)]
    public string? Code { get; init; }

    /// <summary>System error number.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Nj.JsonProperty(NullValueHandling = Nj.NullValueHandling.Ignore)]
    public int? Number { get; init; }

    /// <summary>Correlation id of the request.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Nj.JsonProperty(NullValueHandling = Nj.NullValueHandling.Ignore)]
    public string? TraceId { get; init; }

    /// <summary>Validation errors by member.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Nj.JsonProperty(NullValueHandling = Nj.NullValueHandling.Ignore)]
    public IDictionary<string, IList<string>>? Errors { get; init; }

    /// <summary>Exception type and stack trace; only filled when the host opted in.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Nj.JsonProperty(NullValueHandling = Nj.NullValueHandling.Ignore)]
    public string? Details { get; init; }
}

/// <summary>
/// Builds <see cref="ApiErrorBody"/> instances so the middleware, the controller base and the entity routes answer alike.
/// </summary>
internal static class ApiErrorResponses
{
    internal static string TraceId(HttpContext context) => Activity.Current?.Id ?? context.TraceIdentifier;

    internal static ApiErrorBody From(ApiException exception, HttpContext context) => new()
    {
        Message = exception.Message,
        Code = exception.ErrorCode,
        Number = exception.ErrorNumber == 0 ? null : exception.ErrorNumber,
        TraceId = TraceId(context),
    };

#pragma warning disable CS0618 // TTGException is obsolete but still thrown by older consumer code
    internal static ApiErrorBody From(TTGException exception, HttpContext context) => new()
    {
        Message = exception.Message,
        Code = exception.ErrorCode,
        Number = exception.ErrorNumber == 0 ? null : exception.ErrorNumber,
        TraceId = TraceId(context),
    };
#pragma warning restore CS0618

    internal static ApiErrorBody NotFound(string entityName, object? id, HttpContext context) => new()
    {
        Message = id == null ? $"{entityName} not found" : $"{entityName} not found for passed {id}",
        Code = $"{entityName}_Not_Found".ToUpperInvariant(),
        TraceId = TraceId(context),
    };

    internal static ApiErrorBody Failure(string message, string code, HttpContext context) => new()
    {
        Message = message,
        Code = code,
        TraceId = TraceId(context),
    };

    internal static ApiErrorBody Validation(string message, string code, ValidationResult? result, HttpContext context) => new()
    {
        Message = message,
        Code = code,
        Errors = result?.Errors,
        TraceId = TraceId(context),
    };
}
