param([switch]$Publish)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
dotnet build src/SoftwareGamer.App -c Release -warnaserror
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
dotnet run --project tests/SoftwareGamer.Tests -c Release
if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
if ($Publish) {
  dotnet publish src/SoftwareGamer.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o artifacts/portable
  if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
}
