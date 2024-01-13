namespace TabTabGo.Core.Services;

public interface IMapper<TEntity, TViewModel>
{
    TViewModel Map(TEntity entity);
    TEntity Map(TViewModel viewModel);
    void Map(TEntity entity, TViewModel viewModel);
}