# TabTabGo.Core.Infrastructure

Data-access contracts for TabTabGo systems, in namespace `TabTabGo.Core.Infrastructure.Data`:

- `IGenericReadRepository<TEntity, TKey>`
- `IGenericRepository<TEntity, TKey>`
- `IUnitOfWork`

These are the same signatures as the published `8.0.0-alpha-2` package, rebuilt against `TabTabGo.Core` 8.0.1 (net8.0), so
`using TabTabGo.Core.Infrastructure.Data;` keeps compiling without code changes. Implementations live in `TabTabGo.Data.EF`.
