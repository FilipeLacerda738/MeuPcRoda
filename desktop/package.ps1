param([string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
$project = [System.IO.Path]::GetFullPath($ProjectRoot)
$source = Join-Path $project 'desktop'
$output = Join-Path $project 'release'

& (Join-Path $source 'build.ps1') -ProjectRoot $project
$executable = Join-Path $output 'MeuPcRoda.exe'
$testReport = Join-Path ([System.IO.Path]::GetTempPath()) ('MeuPcRoda-testes-'+[Guid]::NewGuid().ToString('N')+'.json')
try {
    $testProcess = Start-Process -FilePath $executable -ArgumentList @('--self-test',('"'+$testReport+'"')) -WindowStyle Hidden -PassThru -Wait
    if ($testProcess.ExitCode -ne 0) { throw 'Os testes locais falharam.' }
    $testResult = Get-Content -LiteralPath $testReport -Raw | ConvertFrom-Json
    if (-not $testResult.success) { throw 'O relatorio de testes nao confirmou sucesso.' }
    Write-Output ('Testes locais aprovados: '+@($testResult.passed).Count)
} finally {
    if (Test-Path -LiteralPath $testReport) { Remove-Item -LiteralPath $testReport -Force }
}

$sourceArchive = Join-Path $output 'MeuPcRoda-Codigo-Fonte.zip'
$binaryArchive = Join-Path $output 'MeuPcRoda-Windows.zip'
$rootFiles = @('.gitignore','README.md','LICENSE','SECURITY.md') | ForEach-Object { Join-Path $project $_ }
Compress-Archive -LiteralPath (@($source) + $rootFiles) -DestinationPath $sourceArchive -Force
Copy-Item -LiteralPath (Join-Path $source 'LEIA-ME.txt') -Destination (Join-Path $output 'LEIA-ME.txt') -Force
Copy-Item -LiteralPath (Join-Path $project 'LICENSE') -Destination (Join-Path $output 'LICENSE') -Force
Compress-Archive -LiteralPath $executable,(Join-Path $output 'LEIA-ME.txt'),(Join-Path $output 'LICENSE') -DestinationPath $binaryArchive -Force
@($executable,$sourceArchive,$binaryArchive) | ForEach-Object {
    $item = Get-Item -LiteralPath $_
    '{0}  {1}' -f (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash,$item.Name
} | Set-Content -LiteralPath (Join-Path $output 'SHA256.txt') -Encoding ASCII
Get-Item -LiteralPath $executable,$binaryArchive,$sourceArchive | Select-Object Name,Length
