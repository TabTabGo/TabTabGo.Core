# TabTabGo.Core

The TabTabGo foundation for .NET services: the **entity base**, generic **repository / unit of work / base services**,
ASP.NET Core **entity routes**, middlewares, and a JSON layer that works with both **System.Text.Json** and
**Newtonsoft.Json**.

## 1. Overview and release lines

| Line | Runtime | Package version | Notes |
|---|---|---|---|
| **8.x** | .NET 8 (support ends 2026-11-10) | 8.0.1 | bridge release: vulnerable packages cleared, no code changes |
| **10.x** (this branch) | .NET 10 (LTS to 2028-11-14) | **10.0.0** | every package latest stable, all known bugs fixed, STJ + Newtonsoft interop, full tests |
| 11.x | .NET 11 (RC until GA) | 11.0.0-rc.N | retarget of 10.x, see the release notes of that line |

Everything below describes the 10.x line. The TFM is `net10.0` only; there is no multi-targeting.

## 2. Packages

| Package | What it holds |
|---|---|
| `TabTabGo.Core` | `Entity` / `IEntity` (audit fields, `IsEnabled`, `ExtraProperties`), repository and unit-of-work **ports** (`TabTabGo.Core.Data`), service interfaces, `PageList<T>`, `ApiException`, models and view models, `SerializerEngine`, **`TabTabGo.Core.Json`** interop, converters, comparers, extensions, `RandomUtil`, `TTGStopWatch` |
| `TabTabGo.Core.Services` | `BaseReadService<,>`, `BaseService<,>` (OData aware), `JsonStringLocalizer` |
| `TabTabGo.Data.EF` | `GenericReadRepository`, `GenericRepository`, abstract `UnitOfWork`, `EntityBuilder()` model extension |
| `TabTabGo.Core.Api` | `MapEntityRoutes` (minimal API), `EntityControllerBase` (legacy MVC), `ErrorHandlingMiddleware`, `RequestResponseLoggingMiddleware` |
| `TabTabGo.Core.Country` | country data (ISO codes, English and Arabic names) and `CountryService` |
| `TabTabGo.IO.Utilities` | `FileUtility`, `PathUtility` |

`TabTabGo.Core.Infrastructure` is **not published** for 10.x (it was empty). The repository ports live in `TabTabGo.Core`
under the namespace `TabTabGo.Core.Data`.

## 3. Getting started

### 3.1 The model

```csharp
public class Product : Entity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
}

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // audit column lengths, and ExtraProperties stored as compact JSON with change detection
        modelBuilder.Entity<Product>(b =>
        {
            b.HasKey(p => p.Id);
            b.EntityBuilder();
        });
    }
}

public class AppUnitOfWork(AppDbContext context, ILogger<UnitOfWork> logger) : UnitOfWork(context, logger);
```

`EntityBuilder()` stores `ExtraProperties` as compact JSON (null values kept) and registers a value comparer, so editing
the dictionary in place is detected and saved. Older rows written in the indented format are still read.

### 3.2 A service

```csharp
public class ProductService(IUnitOfWork unitOfWork, ILogger<BaseService<Product, int>> logger)
    : BaseService<Product, int>(unitOfWork, logger)
{
    public override Expression<Func<Product, bool>> GetKeyPredicate(int id) => p => p.Id == id;

    public override int GetKey(Product entity) => entity.Id;
}
```

### 3.3 Registration and routes

```csharp
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlServer(connectionString));
builder.Services.AddScoped<IUnitOfWork>(sp => new AppUnitOfWork(sp.GetRequiredService<AppDbContext>(), sp.GetRequiredService<ILogger<UnitOfWork>>()));
builder.Services.AddScoped<IBaseService<Product, int>, ProductService>();
builder.Services.AddOData();
builder.Services.AddAuthentication().AddJwtBearer();
builder.Services.AddAuthorization();

var app = builder.Build();
app.UseTabTabGoErrorHandling(o => o.IncludeExceptionDetails = app.Environment.IsDevelopment());
app.UseAuthentication();
app.UseAuthorization();
app.MapEntityRoutes<Product, int, Product, Product, Product>("products", o =>
{
    o.ControllerPrefix = "api";
    o.Version = "v1";
});
```

The DI container owns the `DbContext`; `UnitOfWork` disposes it only when you construct it with `ownsContext: true`
(outside DI).

## 4. Features

### 4.1 Entities and `ExtraProperties`
`Entity` carries `CreatedBy/Date`, `UpdatedBy/Date`, `IsEnabled` (soft delete) and `ExtraProperties`, a bag for properties
the type does not declare. Both JSON engines flatten the bag into the entity's JSON object. `Populate`, JSON patch and
`Update(id, JsonObject)` put unknown properties there.

### 4.2 Repositories and unit of work
* Reads: `Get`, `GetAsync`, `GetQueryable`, `FirstOrDefault`, `Count`, `AnyAsync`, `GetByKey(Async)` (honours includes),
  `GetPageListAsync`, `GetProjectedPageListAsync` (projection runs in SQL). `QueryFlags`: `DisableTracking`, `IgnoreFixQuery`
  (ignore global query filters), `IsExpandable` (LinqKit), `UseLocal`.
* Writes track only; `IUnitOfWork.SaveChanges(Async)` saves. Transactions: `BeginTransaction(Async)` (the isolation level is
  honoured), `Commit(Async)`, `Rollback(Async)`, `HasActiveTransaction`.
* `GetChanges()` returns `IReadOnlyList<EntityChange>` with **snapshot** values (`State`, `OriginalValues`, `CurrentValues`).
* New port members are default interface methods, so your own implementations of the 8.x interfaces still compile.

**Tracking.** Paged reads (`GetPageListAsync`, `GetProjectedPageListAsync`) and OData reads are **untracked** by default.
Pass `QueryFlags.EnableTracking` when you want tracked entities.

**Paging contract.** `pageNumber` going **in** is a **0-based page index**; `PageList<T>.PageNumber` coming **out** is
**1-based**. Without an `orderBy` the primary key is used so pages are stable. A negative index throws.

### 4.3 Services
`BaseReadService` / `BaseService` give list, page, OData search, get by key, view models, create, full update,
`UpdateChanges`, JSON patch, partial JSON update and soft delete, with hooks (`IsValidTo*`, `Pre*`, `Post*`).
Failures are `ApiException`s: **404** when the entity does not exist, **400** when a validation hook rejects the operation
(`Create` 2001, `Update` 4001/4002/4003, `Delete` 3001/3002). `CanDelete(ids)` returns the real answer and
`GetDeleteValidationAsync(ids)` tells which entity blocks the delete. `Update(id, entity)` rejects a body whose key differs
from the route key (400, 4003).

### 4.4 OData
OData options are applied with case-insensitive property names; `$select` is translated to SQL. The service understands the
custom function `xmlExist(path, 'value')` **only under `and`** (several calls are ANDed, never ORed): below an `or` or a `not` it answers **400**. The path must
be a mapped property path, the value is one quoted literal that travels as the SQL parameter `@pN` and the generated SQL
compares the text of the elements: `column.exist('//*[text() = sql:variable("@p0")]') = 1`.

### 4.5 Entity routes (`MapEntityRoutes`)

| Method | Route (`{prefix}` = `ControllerPrefix/Version/` when set) | Response |
|---|---|---|
| GET | `{prefix}{name}` | `PageList` (view models when an `IMapper` is registered) |
| GET | `{prefix}{name}/{id}` | entity or view model; `lastUpdatedDate`, `expand` |
| POST | `{prefix}{name}/export/{format}` | file; **501** until the service overrides `ExportFile` |
| POST | `{prefix}{name}` | **201** with `Location: {prefix}{name}/{id}` |
| PATCH | `{prefix}{name}/{id}` | JSON Patch document (`application/json-patch+json` or `application/json`) |
| DELETE | `{prefix}{name}/{id}` | `{ message, code }`; **412** when `CanDelete` is false |

* **DELETE is a soft delete**: it sets `IsEnabled = false`. The row stays, and GET by id and search still return it unless
  your scope criteria or a query filter exclude disabled entities.
* **PATCH guards.** A patch cannot touch the key, the audit fields, `IsEnabled` or related entities (400
  `PROTECTED_PROPERTY`; names are resolved to the CLR property first, so `tenant_id`, `TENANTID` and `tenantid` are the same
  property). Override `IsProtectedProperty` in your service to protect more, for example a tenant column. Adding extra
  properties is bounded (100 keys, 100-character names, 16 384-character values). After the patch the entity must still
  be inside `GetFixCriteria`: the service saves inside a transaction (run through the provider's execution strategy, so
  SQL Server and Npgsql retrying strategies work; `PostUpdate` runs after the commit), asks the database (so foreign keys
  count) and rolls back with a 404 when the entity left the scope.
  * **Scope fails closed.** A service whose `UpdateChanges` cannot enforce a scope (the default interface body) answers 500
    `SCOPE_NOT_ENFORCED` instead of saving outside it. Before the service is called the route also evaluates the scope in
    memory: a definite "outside" is a 404, a predicate that cannot be evaluated in memory (a navigation that is not loaded)
    is left to the database check.
  * **Scope-defining members cannot be patched.** The members `GetFixCriteria` refers to (the tenant column, a navigation and
    its `<Navigation>Id` foreign key) are rejected with 400 `PROTECTED_PROPERTY` by default; override
    `IsScopeDefiningProperty` to allow moving entities between scopes.
  * **Hidden properties are not writable.** A property with `[JsonIgnore]` (System.Text.Json "always" or Newtonsoft) or
    `[NotMapped]` is protected too; `ExtraProperties` stays patchable, with its limits.
  * `Update(id, JsonObject)` checks the extra-properties limits on the merged bag, not only per key.
  * Callers that use `IBaseService.UpdateChanges(id, entity, ct)` or `Update(...)` **directly** (not through the routes) must
    apply their own scope: only the scoped overload checks it. Likewise bulk delete counts the keys inside the scope and then
    deletes by key; a row that leaves the scope between the two calls is still deleted. Both are by design of the service layer.
  * **Bulk keys.** `CountByKeys` and bulk delete build the store's primary-key `IN` list (a query parameter, not inlined);
    composite keys fall back to one `GetKeyPredicate` per key. Override `PreferServiceKeyPredicate` when your service's
    `GetKeyPredicate` is not "primary key equals key".
  * **Filters are bounded.** `$filter` longer than 4 096 characters or nested deeper than 32 levels answers 400.
* **Bulk delete** (`EntityControllerBase.DeleteEntities`) accepts at most 500 ids (400 `TOO_MANY_IDS`), looks them up with one
  `IN` list, and checks the scope with one count query.
* Every route requires authorization (`RequireAuthorization()` on the group); there is no PUT.
* Services are resolved **per request**, so scoped services and their `DbContext` are never shared between requests.
* **Scope criteria.** `SearchFixCriteria`, `GetFixCriteria` and `ExportFixCriteria` are always applied. `GetFixCriteria`
  also guards **PATCH and DELETE**: an entity outside the scope answers 404 on every route.
* Without a mapper the create request must be the entity type; otherwise `MapEntityRoutes` throws at startup.
* The PATCH body is read with Newtonsoft because `JsonPatchDocument<T>` is a Newtonsoft type.
* The `controllerName` you pass is the route segment: use lower-case kebab and the plural (`order-lines`).

**`EntityControllerBase<…>`** is the legacy MVC base class. It stays for existing controllers; new code uses
`MapEntityRoutes`. Override `SearchFixCriteria`, `GetFixCriteria` and `ExportFixCriteria` to scope it the same way. The host
must be able to bind `JsonPatchDocument<T>` (for example `AddNewtonsoftJson`).

### 4.5.1 Contracts you implement
* `ISecurityService<TUserKey, TTenantKey>`: who the caller is (user, tenant, roles, permissions). Core only declares it;
  you implement it over your identity system. `GetClientId()` no longer exists.
* `IMapper<TEntity, TViewModel>`: maps entity to view model, request to entity, and pages. Register it to return view models;
  without one the entity is the view model and, for `EntityControllerBase`, `CreateEntity` answers 500 `MAPPER_REQUIRED`
  unless the create request is the entity (`MapEntityRoutes` fails at startup instead).
* `IValidationService<TEntity, TRequest>`: validates create requests and patched entities; a result with `IsValid == false`
  answers 400 with the errors. Register none to skip validation.

### 4.6 Middlewares
* `app.UseTabTabGoErrorHandling()` turns exceptions into `{ number, code, message, traceId }`. `ApiException` keeps its
  status code, code and number; anything else is a generic **500**. The exception object and stack trace are never
  returned unless `IncludeExceptionDetails` is on (Development only). The exception is logged once.
* `app.UseRequestResponseLogging(options)` logs headers and, optionally, bodies. `Authorization`, `Cookie`, `Set-Cookie`,
  `X-Api-Key` and similar headers are replaced by `***`; bodies are cut at `MaxBodyLength` (32 768 characters); bodies are
  log arguments, never message templates; the response is written through, not buffered.

### 4.7 Errors and responses
`ApiException` (message, `HttpStatusCode`, `ErrorCode`, `ErrorNumber`, `Level`, `RootException`; `TTGException` is obsolete).
`ErrorResponse` never serializes its `Exception`. `GeneralResponse` has `message`, `code`, `data` and an `Extra` bag that is
flattened into the object.

### 4.8 Localisation and countries
`JsonStringLocalizer` reads `{culture}.json` from the embedded resources of the assembly in
`JsonStringLocalizerOptions` (default `TabTabGo.Core.Country`) and from the optional `Resources` folder next to the
application, whose entries **override** the embedded ones. Each file is parsed once per process. Lookup: culture, parent
cultures, other cultures of the same language (`ar` finds `ar-AE`), then the default (`en`). Culture names must look like
cultures (`xx`, `xx-YY`) and match an existing resource; anything else falls back to the default and is never used to build
a path. `ICultureStringLocalizer.GetString(name, culture, args)` looks up an explicit culture; `this[name, args]` formats the
arguments.

`ICountryService.GetCountry(code, culture)` finds a country by alpha-2 or alpha-3 code, case-insensitively. The data is
embedded; no `Data` folder is needed.

### 4.9 Utilities
* `RandomUtil` uses `RandomNumberGenerator` (passwords, activation and reset keys, user stamps).
* `TTGStopWatch` (`TabTabGo.Core.Utilities`; `TTG.Core.TTGStopWatch` is an obsolete shim) records laps and writes them with
  `CommitLog`; it throws `ObjectDisposedException` after `Dispose`.
* Converters: `JsonEnumConverter<T>` (`[JsonEnumName]`; reads attribute name, member name or number),
  `TimespanToSecondsConverter` (a JSON number of seconds; reads numbers and numeric strings).
* Comparers: `EntityComparer`, `JsonNodeComparer` (order-insensitive, `IgnoredProperties`), `JsonArrayComparer`.
* Extensions: `JsonExtensions.Populate` (merges only the properties present), `EntityExtensions.CopyObject/Clone`,
  `DictionaryExtensions`, `StringExtensions`, `XmlExtensions`, `AssemblyExtensions.ExtractResources`, `ExportExtensions`.
* `SerializerEngine`: shared **read-only** `JsonSerializationSettings`, `CreateJsonSerializerOptions()` for a mutable copy,
  XML/JSON/dynamic/dictionary/enum helpers (XML repeated elements become arrays, attributes become `@name`).
* `TabTabGo.IO.Utilities`: `FileUtility` (temp files, `TryDeleteFile`, whole-file reads, `EnsureUniqueFilename`) and
  `PathUtility`.

## 5. JSON: System.Text.Json first, Newtonsoft available

STJ is the primary engine. Newtonsoft stays because `JsonPatchDocument<T>` needs it and many hosts still use it. The
namespace `TabTabGo.Core.Json` converts between the two.

| Helper | Purpose |
|---|---|
| `JsonInterop.ToJToken / ToJObject / ToJArray` | `JsonElement`, `JsonNode`, `JsonDocument`, `JsonObject`, `JsonArray` to Newtonsoft |
| `JsonInterop.ToJsonElement / ToJsonNode / ToJsonObject / ToJsonArray / ToJsonDocument` | Newtonsoft to STJ (`ToJsonElement` returns a detached clone; `ToJsonDocument` is yours to dispose) |
| `DeserializeWithSystemTextJson<T>`, `DeserializeWithNewtonsoft<T>`, `SerializeToJTokenWithSystemTextJson`, `SerializeToJsonNodeWithNewtonsoft` | cross-engine object bridges |
| `ToClrValue`, `NormalizeJsonValues` | plain CLR values for property bags such as `ExtraProperties` |
| `ParseJToken`, `ParseJsonNode`, `ToJsonString` | strings |
| `JsonEquals` | order-insensitive, number-by-value equality across engines |
| `JsonDefaults` | `CreateSystemTextJsonOptions()`, `CreateNewtonsoftSettings()`, `AddTabTabGoConverters(...)` |

Fidelity rules: conversion goes through JSON text; ISO dates stay **strings** (Newtonsoft never turns them into
`DateTime`); `JsonInteropOptions.FloatParseHandling = Decimal` keeps money exact; `MaxDepth` defaults to 64; duplicate
STJ properties resolve last-wins.

Register the converters on options you own:

```csharp
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.AddTabTabGoConverters());
builder.Services.AddControllers().AddNewtonsoftJson(o => o.SerializerSettings.AddTabTabGoConverters());
```

With the STJ converter a `JObject` inside `ExtraProperties` is written as the object it is (it used to be written as nested
empty arrays); with the Newtonsoft converters `JsonObject`, `JsonArray`, `JsonElement` and `JsonDocument` properties
work under Newtonsoft.

## 6. Testing

```
test/
  TabTabGo.Core.TestKit/          fakes and fixtures (SQLite and InMemory fixtures, OData helper, TestHost app); not packed
  TabTabGo.Core.Tests/            Core
  TabTabGo.Data.EF.Tests/         repositories, unit of work, EntityBuilder
  TabTabGo.Core.Services.Tests/   services, OData tokenizer, localizer
  TabTabGo.Core.Api.Tests/        routes, controller base, middlewares
  TabTabGo.Core.Country.Tests/    countries and legacy helpers
  TabTabGo.IO.Utilities.Tests/    file and path helpers
```

xunit.v3 on Microsoft.Testing.Platform. Relational behaviour runs on **SQLite in-memory** (DateTimeOffset is mapped with
`DateTimeOffsetToBinaryConverter` because SQLite cannot order it); EF InMemory is used only where SQL is irrelevant. No
mocking library: fakes are hand-written.

```bash
dotnet test --solution TabTabGo.Core.sln -c Release
dotnet test --project test/TabTabGo.Core.Tests -c Release --coverlet --coverlet-output-format cobertura
```

## 7. Upgrading

### From 8.0.1-alpha-01 / 8.0.1
Binary and behaviour changes (additive changes are not listed):

| Id | Change |
|---|---|
| B1 | `net10.0` only |
| B2 | EF Core 8 to 10, AspNetCore.OData 8 to 9, JsonPatch 8 to 10 |
| B3 | Swashbuckle is no longer a dependency of `TabTabGo.Core.Api` |
| B4 | `ErrorHandlingMiddleware` takes a logger and options: use `UseTabTabGoErrorHandling()` or `UseMiddleware<>` |
| B5 | `GetPageListAsync`: 1-based `PageNumber` out, negative index throws, default key order |
| B6 | skip/take are applied without `orderBy` |
| B7 | error bodies carry no `exception`; `number`, `code`, `traceId` are populated; real status codes |
| B8, B9 | services answer 404 / 400 as `ApiException`; `CanDelete(ids)` is real; `Delete(ids)` validates |
| B10 | request logging redacts credential headers and truncates bodies |
| B11 | `JsonStringLocalizer` reads embedded resources (+ optional file override); arguments are formatted, not read as a culture: use `ICultureStringLocalizer` |
| B12 | `TTGStopWatch` moved to `TabTabGo.Core.Utilities` (obsolete shim in `TTG.Core`) |
| B13 | `UnitOfWork` / repositories no longer dispose a DI-owned `DbContext`; a null context throws; pass `ownsContext: true` outside DI |
| B14 | Newtonsoft `[JsonExtensionData]` on `ExtraProperties` and the other bags |
| B15 | `Clone` returns a new instance; `CopyObject` honours ignored and read-only properties |
| B16 | `ConvertToDictionary(usePathAsKey: true)` returns data |
| B17 | `SerializerEngine` options are read-only; `ConvertJsonToDictionary`, `XmlToJson`, `EnumToJson`, dictionary to JSON fixed |
| B18 | OData custom functions under `or` / `not` answer 400 |
| B20 | `IEntity.CreatedBy/UpdatedBy` are `string?`; `ExtraProperties` values are `object?` |
| B21 | `GetChanges()` returns `IReadOnlyList<EntityChange>` |
| B22, B23 | `EntityComparer` compares properties; `ExtraProperties` edits are detected and stored compact with nulls kept |
| B24 | `Populate`, `Update(id, JsonObject)`, `PatchEntityByJson` merge only the provided properties |
| B25 | patch validation is no longer inverted |
| B26 | `MapEntityRoutes`: per-request services; `SearchFixCriteria`, `ControllerPrefix`, `Version` applied |
| B28 | `TimespanToSecondsConverter` writes a number |
| B30 | `JsonNodeComparer` honours `IgnoredProperties` |
| B31, B32 | `GeneralResponse.Data` serialises as `data`, new `Extra` bag; `ErrorResponse.Exception` is not serialised |
| B33 | `TabTabGo.Core.Infrastructure` is not published |
| B34 | Country data embedded; codes matched case-insensitively |
| B35 | `TabTabGo.IO.Utilities` is published from this repository |
| B36 | `EntityControllerBase.CreateEntity` `Location` is the resource URL |
| B37 | `ISecurityService.GetClientId()` and `ITenant<TKey>.Settings` are removed |

Additional 10.0.0 changes: paged and OData reads are untracked by default, and `QueryFlags.EnableTracking` asks for tracking
(B38); `xmlExist` compares element text with the value instead of running a caller-supplied XPath (B39); `ControllerPrefix` and
`Version` now apply to the generated routes, so consumers that set them get new URLs (B26); `EntityControllerBase` has new
virtual `SearchFixCriteria`, `GetFixCriteria` and `ExportFixCriteria`.

Found while writing the tests (also fixed): scope criteria were missing on search, patch and delete (D-34); the `xmlExist`
function pasted user input into SQL (D-35); the localizer built paths from the caller's culture (D-36); `GetViewModelsDataTable`
could never add a row (D-37); `DynamicToJson` failed to bind at run time (D-38); the minimal-API PATCH could not bind a JSON
Patch body (D-39); `Update(id, entity)` with a different body key failed inside EF (now 400, D-40).

Not changed on purpose: `BaseReadService.GetByKey(lastUpdatedDate)` returns the entity only when it has **not** been updated
since that date (`lastUpdatedDate > entity.UpdatedDate`); this is the behaviour of 8.x and is now documented as such;
`GetIncludedProperties` only detects `ICollection<T>` / `IEnumerable<T>` / array typed navigation properties.

### From 6.x
`TabTabGo.Core.Infrastructure.Data` becomes `TabTabGo.Core.Data`; `JObject` overloads become `JsonObject`; `TTGException`
becomes `ApiException`; `ISecurityService` and `IParseClaimsPrincipal` are generic.

## 8. Building and releasing

* `global.json` pins SDK 10.0.401 and the Microsoft.Testing.Platform runner. `Directory.Build.props` holds the TFM,
  language version, nullable, NuGet audit (vulnerabilities fail the build) and package metadata;
  `Directory.Packages.props` holds every package version (central package management).
* `dotnet build -c Release -warnaserror` must be clean. `dotnet pack -c Release` produces the six packages; package
  validation compares against `8.0.1-alpha-01`.
* Publishing is driven by version tags (`v10.0.0`, `v10.0.0-rc.1`).

## 9. License

[MIT](LICENSE)
