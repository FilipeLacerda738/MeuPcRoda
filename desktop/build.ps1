param([string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
$project = [System.IO.Path]::GetFullPath($ProjectRoot)
$source = Join-Path $project 'desktop'
$output = Join-Path $project 'release'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath (Join-Path $framework 'csc.exe'))) { $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319' }
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Compilador do .NET Framework não encontrado.' }
New-Item -ItemType Directory -Force -Path $output | Out-Null
Add-Type -AssemblyName System.Drawing
$bitmap = [System.Drawing.Bitmap]::new(64,64)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([System.Drawing.Color]::FromArgb(194,247,106))
$font = [System.Drawing.Font]::new('Segoe UI',35,[System.Drawing.FontStyle]::Bold,[System.Drawing.GraphicsUnit]::Pixel)
$brush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(21,32,12))
$graphics.DrawString('M',$font,$brush,[single]8,[single]7)
$iconFile = Join-Path ([System.IO.Path]::GetTempPath()) ('MeuPcRoda-'+[Guid]::NewGuid().ToString('N')+'.ico')
$iconHandle = $bitmap.GetHicon()
$icon = [System.Drawing.Icon]::FromHandle($iconHandle)
$stream = [System.IO.File]::Create($iconFile)
try { $icon.Save($stream) } finally { $stream.Dispose(); $icon.Dispose(); $brush.Dispose(); $font.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
$argsList = @('/nologo','/target:winexe','/platform:anycpu','/optimize+','/debug-','/utf8output','/codepage:65001',('/out:'+(Join-Path $output 'MeuPcRoda.exe')),('/win32manifest:'+(Join-Path $source 'app.manifest')),('/win32icon:'+$iconFile),('/resource:'+(Join-Path $source 'MainWindow.xaml')+',MainWindow.xaml'))
foreach ($name in @('System.dll','System.Core.dll','System.Management.dll','System.Net.Http.dll','System.Web.Extensions.dll','System.Xaml.dll')) { $argsList += '/reference:'+(Join-Path $framework $name) }
foreach ($name in @('PresentationFramework.dll','PresentationCore.dll','WindowsBase.dll')) { $argsList += '/reference:'+(Join-Path $framework ('WPF\'+$name)) }
$argsList += @((Join-Path $source 'Core.cs'),(Join-Path $source 'App.cs'),(Join-Path $source 'Diagnostics.cs'))
try {
    & $compiler @argsList
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o aplicativo.' }
} finally {
    if (Test-Path -LiteralPath $iconFile) { Remove-Item -LiteralPath $iconFile -Force }
}
Get-Item -LiteralPath (Join-Path $output 'MeuPcRoda.exe') | Select-Object FullName,Length
