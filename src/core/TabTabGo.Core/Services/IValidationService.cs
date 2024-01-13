namespace TabTabGo.Core.Services
{
    public interface IValidationService<in TEntity, in TRequest>
    {
        ValidationResult Validate(TRequest request);
        ValidationResult Validate(TEntity request);
    }
}