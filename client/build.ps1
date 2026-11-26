$ErrorActionPreference = 'Stop'

$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath (Join-Path $framework 'csc.exe'))) {
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'Building the client requires Windows with .NET Framework 4.8.'
}

$destination = Join-Path $PSScriptRoot 'dist'
[System.IO.Directory]::CreateDirectory($destination) | Out-Null
$executable = Join-Path $destination 'Translate.exe'
$window = Join-Path $PSScriptRoot 'MainWindow.xaml'

$arguments = @(
    '/nologo'
    '/target:winexe'
    '/platform:anycpu'
    '/optimize+'
    '/codepage:65001'
    "/out:$executable"
    "/resource:$window,Translate.MainWindow.xaml"
)
foreach ($assembly in @(
    'System.Net.Http.dll'
    'System.Runtime.Serialization.dll'
    'System.Xaml.dll'
    'WPF\PresentationCore.dll'
    'WPF\PresentationFramework.dll'
    'WPF\WindowsBase.dll'
)) {
    $arguments += '/reference:' + (Join-Path $framework $assembly)
}
$arguments += Join-Path $PSScriptRoot 'App.cs'
$arguments += Join-Path $PSScriptRoot 'ServerClient.cs'

& $compiler @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Translate.exe build failed with exit code $LASTEXITCODE."
}
Write-Output $executable
