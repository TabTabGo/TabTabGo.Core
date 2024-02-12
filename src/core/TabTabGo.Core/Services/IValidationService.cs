namespace TabTabGo.Core.Services
{
    public interface IValidationService<in TEntity, in TRequest>
    {
        Task<ValidationResult> Validate(TEntity entity, CancellationToken cancellationToken);
        Task<ValidationResult> Validate(TRequest entityRequest, CancellationToken cancellationToken);
    }
}