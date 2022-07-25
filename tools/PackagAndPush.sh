#!/bin/bash
# pass verision number as first argument
set packageVersion=$1
echo "Version number: $packageVersion"
# set nuget API key
export NUGET_API_KEY=oy2ecvf4d7qzb6jvfgtaoovta6rkynderpuuqnhpo4ew2q
# check if version number is not set then ask for it
if [ -z "$packageVersion" ]; then
  echo "Enter version number:"
  read packageVersion
fi

# Build the TabTabGo.Core
dotnet build ./src/core/TabTabGo.Core/TabTabGo.Core.csproj -c Release --version-suffix $packageVersion
# Publish the TabTabGo.Core
dotnet nuget push ./src/core/TabTabGo.Core/bin/Release/TabTabGo.Core.$packageVersion.nupkg -s https://api.nuget.org/v3/index.json -k $NUGET_API_KEY

# build the TabTabGo.Core.Services
dotnet build ./src/core/TabTabGo.Core.Services/TabTabGo.Core.Services.csproj -c Release --version-suffix $packageVersion
# Publish the TabTabGo.Core.Services
dotnet nuget push ./src/core/TabTabGo.Core.Services/bin/Release/TabTabGo.Core.Services.$packageVersion.nupkg -s https://api.nuget.org/v3/index.json -k $NUGET_API_KEY

#build the TabTabGo.Core.Api
dotnet build ./src/core/TabTabGo.Core.Api/TabTabGo.Core.Api.csproj -c Release --version-suffix $packageVersion
# Publish the TabTabGo.Core.Api
dotnet nuget push ./src/core/TabTabGo.Core.Api/bin/Release/TabTabGo.Core.Api.$packageVersion.nupkg -s https://api.nuget.org/v3/index.json -k $NUGET_API_KEY

# build TabTabGo.Core.Infrastructure
dotnet build ./src/core/TabTabGo.Core.Infrastructure/TabTabGo.Core.Infrastructure.csproj -c Release --version-suffix $packageVersion
# Publish the TabTabGo.Core.Infrastructure
dotnet nuget push ./src/core/TabTabGo.Core.Infrastructure/bin/Release/TabTabGo.Core.Infrastructure.$packageVersion.nupkg -s https://api.nuget.org/v3/index.json -k $NUGET_API_KEY

# build TabTabGo.Data.EF
dotnet build ./src/data/TabTabGo.Data.EF/TabTabGo.Data.EF.csproj -c Release --version-suffix $packageVersion
# Publish the TabTabGo.Core.Infrastructure
dotnet nuget push ./src/data/TabTabGo.Data.EF/bin/Release/TabTabGo.Data.EF.$packageVersion.nupkg -s https://api.nuget.org/v3/index.json -k $NUGET_API_KEY

