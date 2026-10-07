[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $csc)) { throw 'Missing .NET Framework 4.x C# compiler.' }
foreach ($name in @('PortableManager', 'PotPlayerPortable')) {
    $output = if ($name -eq 'PortableManager') { 'PotPlayer管理.exe' } else { 'PotPlayerPortable.exe' }
    & $csc /nologo /target:winexe /optimize+ /reference:System.Windows.Forms.dll /reference:System.Drawing.dll (('/out:') + (Join-Path $PSScriptRoot $output)) (Join-Path $PSScriptRoot ('Tools\' + $name + '.cs'))
    if ($LASTEXITCODE -ne 0) { throw ('Compile failed: ' + $name) }
}