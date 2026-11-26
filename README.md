# .NET Core Library

## Overview
This .NET 8 Core Library is designed as a foundational framework that provides essential, reusable functionalities for various .NET projects. It encapsulates abstract implementations of CRUD operations, streamlining development processes across diverse applications.

### Features
- **Abstract CRUD Repository**: Generic repository implementation for basic Create, Read, Update, and Delete operations, adaptable to various data models.
- **Abstract CRUD Services**: Business logic layer abstracting CRUD operations, enabling efficient data processing and transformations.
- **Abstract CRUD API Controllers**: Standardized API controllers for handling CRUD operations, ensuring consistency across different .NET web applications.

## What changed in 8.0.1

- **Security: exceptions never reach API clients.** `ErrorResponse.Exception`, `ErrorResponseViewModel.Exception` and
  `ResponseViewModel.Exception` are obsolete, never populated and never serialised; unexpected errors answer a generic message
  and the full exception is logged on the server.
- **`ErrorHandlingMiddleware` answers with the exception's own status code.** An `ApiException` (or the obsolete `TTGException`)
  keeps its HTTP status, code and number (it used to be 500 for everything). Bodies are `{number, code, message}`; any other
  exception is a 500 with a generic message. BookingCentral uses this middleware.
- **Patch validation is no longer inverted** (backport of B25): `EntityControllerBase.PatchEntity` answered 400 for a valid patch and
  saved an invalid one; it now rejects an invalid patch with 400 `INVALID_PATCH_REQUEST` and saves a valid one. The minimal-API PATCH
  route carries the same one-line fix, but the route table cannot start on 8.x (see the known issue below).
- `TabTabGo.Core.Infrastructure` is back (the three `TabTabGo.Core.Infrastructure.Data` interfaces of 8.0.0-alpha-2), as a
  compatibility-only package.

## Known issues in 8.0.1

**Known issue (D-12): `MapEntityRoutes` does not start on 8.x.** Building the endpoints throws `InvalidOperationException: Body was
inferred but the method does not allow inferred body parameters`, because AspNetCore.OData 8.x has no minimal-API binding for
`ODataQueryOptions<T>` (added in 9.4.0). This affects every 8.0.x release. Use `EntityControllerBase`, or move to TabTabGo.Core.Api
10.0.0 (AspNetCore.OData 9.5.0).

## Getting Started

### Prerequisites
- .NET 8 SDK
- Familiarity with C# and .NET project structures

### Installation
To include this Core Library in your .NET project, add it as a dependency in your `csproj` file or use the package manager. Replace `YourProjectName` with your actual project's name.

```bash
dotnet add YourProjectName package CoreLibraryName --version x.x.x
```
Or you can manually clone the repository and reference it in your project.

### Usage
TBA

### Contributing
Contributions to the Core Library are greatly appreciated. Please read CONTRIBUTING.md for details on our code of conduct and the process for submitting pull requests to us.

### License
This project is licensed under the MIT License.

