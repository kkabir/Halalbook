# Applies pending EF Core migrations to the production SQL Server database.
# Reads the connection string from src/HalalBook.Api/appsettings.Production.json (git-ignored).
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$settings = Join-Path $root 'src/HalalBook.Api/appsettings.Production.json'
$cs = (Get-Content $settings -Raw | ConvertFrom-Json).ConnectionStrings.Default
if (-not $cs -or $cs -match 'YOUR_DB_PASSWORD') { throw "Set ConnectionStrings:Default in $settings first." }

Push-Location $root
try {
    dotnet tool restore
    dotnet tool run dotnet-ef database update --project src/HalalBook.Api --connection $cs
}
finally { Pop-Location }
