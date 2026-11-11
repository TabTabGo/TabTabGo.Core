namespace TabTabGo.Core.Services;

/// <summary>
/// Validates entities and requests.
/// </summary>
/// <typeparam name="TEntity">Entity type</typeparam>
/// <typeparam name="TRequest">Request type</typeparam>
public interface IValidationService<in TEntity, in TRequest>
{
    /// <summary>Validates an entity.</summary>
    Task<ValidationResult> Validate(TEntity entity, CancellationToken cancellationToken);

    /// <summary>Validates a request.</summary>
    Task<ValidationResult> Validate(TRequest entityRequest, CancellationToken cancellationToken);
}
