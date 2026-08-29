:: some utility commands for debugging issues running the app
:: dotnet --list-runtimes
:: dotnet list package
:: dotnet list package --outdated

:: publish Shell and Gui into same output folder and zip it for different runtimes, see https://learn.microsoft.com/en-us/dotnet/core/rid-catalog#known-rids for others
dotnet msbuild publish.csproj -target:CleanPublishBothZipped -verbosity:detailed -p:RuntimeIdentifier=win-x64
dotnet msbuild publish.csproj -target:CleanPublishBothZipped -verbosity:detailed -p:RuntimeIdentifier=linux-x64
dotnet msbuild publish.csproj -target:CleanPublishBothZipped -verbosity:detailed -p:RuntimeIdentifier=osx-arm64

:: clean output (to prevent build fragment bleed when rebuilding different parts of the same version)
:: dotnet msbuild publish.csproj -target:CleanOutput -verbosity:detailed -p:RuntimeIdentifier=win-x64

:: publish and zip Shell or Gui individually
::dotnet msbuild publish.csproj -target:PublishShell -verbosity:detailed -p:RuntimeIdentifier=win-x64
::dotnet msbuild publish.csproj -target:PublishGui -verbosity:detailed -p:RuntimeIdentifier=win-x64
::dotnet msbuild publish.csproj -target:ZipOutput -verbosity:detailed -p:RuntimeIdentifier=win-x64
