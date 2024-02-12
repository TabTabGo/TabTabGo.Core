using TabTabGo.Core.Models;

namespace TabTabGo.Core.Services;

public interface IMapper<TEntity, TViewModel> 
    where TEntity : class, IEntity
    where TViewModel : class
{
    TViewModel MapToViewModel(TEntity entity);
    TEntity MapToEntity(TViewModel viewModel);
    TEntity MapFromRequest<TRequest>(TRequest viewModel) where TRequest : class;
    void PopulateViewModel(TEntity entity, TViewModel viewModel);
    PageList<TViewModel> MapPaging(PageList<TEntity> entitiesResult);
    IEnumerable<TViewModel> MapList(IEnumerable<TEntity> entitiesResult);
}