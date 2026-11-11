using TabTabGo.Core.Models;

namespace TabTabGo.Core.Services;

/// <summary>
/// Maps between an entity and its view model.
/// </summary>
/// <typeparam name="TEntity">Entity type</typeparam>
/// <typeparam name="TViewModel">View model type</typeparam>
public interface IMapper<TEntity, TViewModel>
    where TEntity : class, IEntity
    where TViewModel : class
{
    /// <summary>Maps an entity to a view model.</summary>
    TViewModel MapToViewModel(TEntity entity);

    /// <summary>Maps a view model to an entity.</summary>
    TEntity MapToEntity(TViewModel viewModel);

    /// <summary>Maps a request object to an entity.</summary>
    TEntity MapFromRequest<TRequest>(TRequest viewModel) where TRequest : class;

    /// <summary>Copies entity values onto an existing view model.</summary>
    void PopulateViewModel(TEntity entity, TViewModel viewModel);

    /// <summary>Maps a page of entities to a page of view models.</summary>
    PageList<TViewModel> MapPaging(PageList<TEntity> entitiesResult);

    /// <summary>Maps entities to view models.</summary>
    IEnumerable<TViewModel> MapList(IEnumerable<TEntity> entitiesResult);
}
