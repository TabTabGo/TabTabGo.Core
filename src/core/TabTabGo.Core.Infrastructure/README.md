# TabTabGo.Core.Infrastructure

A compatibility-only contract set for consumers of the `8.0.0-alpha-2` package. It holds the three data-access interfaces in
namespace `TabTabGo.Core.Infrastructure.Data`, with the signatures that package shipped:

- `IGenericReadRepository<TEntity, TKey>`
- `IGenericRepository<TEntity, TKey>`
- `IUnitOfWork`

so `using TabTabGo.Core.Infrastructure.Data;` keeps compiling against Core 8.0.1 (net8.0) without code changes.

There is no implementation of these interfaces in this repository. `TabTabGo.Data.EF` implements the interfaces of
`TabTabGo.Core.Data` (same names, different namespace and slightly different signatures) and does not reference this package.
New code should use `TabTabGo.Core.Data`; a file that needs both namespaces must alias one of them
(`using CoreData = TabTabGo.Core.Data;`).
