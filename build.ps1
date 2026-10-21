param([switch]$Test)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$localSdk = Join-Path $PSScriptRoot '.tools\dotnet\dotnet.exe'
$dotnetCommand = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { (Get-Command dotnet -ErrorAction Stop).Source }
$sdkVersion = & $dotnetCommand --version
if ([int]($sdkVersion.Split('.')[0]) -lt 10) { throw 'Le SDK .NET 10 est nécessaire pour compiler Checker San.' }
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.tools\cli'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.tools\packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$originalAppData = $env:APPDATA
try {
    $env:APPDATA = Join-Path $PSScriptRoot '.tools\appdata'
    if ($Test) {
        foreach ($project in @('FolderChecker.Tests', 'FolderChecker.App.Tests')) {
            & $dotnetCommand restore ".\$project\$project.csproj" --configfile '.\NuGet.Config'
            if ($LASTEXITCODE -ne 0) { throw "Restore failed: $project" }
            & $dotnetCommand run --project ".\$project\$project.csproj" -c Release --no-restore
            if ($LASTEXITCODE -ne 0) { throw "Tests failed: $project" }
        }
    }
    & $dotnetCommand restore '.\FolderChecker.App\FolderChecker.App.csproj' -r win-x64 --source 'https://api.nuget.org/v3/index.json' -p:PublishSingleFile=true -p:SelfContained=true
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
    & $dotnetCommand publish '.\FolderChecker.App\FolderChecker.App.csproj' -c Release -r win-x64 --self-contained true --no-restore -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o '.\dist\CheckerSan'
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    Copy-Item -LiteralPath '.\README.md' -Destination '.\dist\CheckerSan\Mode-emploi.md' -Force
    # Package only the intended deliverables; leave any user-added archives untouched.
    Compress-Archive -LiteralPath @('.\dist\CheckerSan\CheckerSan.exe', '.\dist\CheckerSan\Mode-emploi.md') -DestinationPath '.\dist\CheckerSan-Windows-x64.zip' -Force
    Get-FileHash -LiteralPath '.\dist\CheckerSan\CheckerSan.exe' -Algorithm SHA256 | Format-List
    Write-Output 'Prêt : dist\CheckerSan\CheckerSan.exe et dist\CheckerSan-Windows-x64.zip'
}
finally { $env:APPDATA = $originalAppData }
