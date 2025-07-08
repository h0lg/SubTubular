:: some utility commands for debugging issues running the app
:: dotnet --list-runtimes
:: dotnet list package
:: dotnet list package --outdated

:: clean output (to prevent build fragment bleed when rebuilding different parts of the same version)
dotnet msbuild publish.csproj /t:CleanOutput /v:d

:: publish Shell
dotnet msbuild publish.csproj /t:PublishShell /v:d

:: publish Gui
dotnet msbuild publish.csproj /t:PublishGui /v:d

:: zip the output folder
dotnet msbuild publish.csproj /t:ZipOutput /v:d
