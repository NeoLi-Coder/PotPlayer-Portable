[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$RootPath,
    [Parameter(Mandatory=$true)][string]$Action,
    [string]$SourcePath,
    [string]$DestinationPath,
    [ValidateSet('Portable','Config')][string]$PackageKind='Portable',
    [ValidateSet('Zip','Directory')][string]$Format='Zip',
    [switch]$IncludeIcaros,
    [switch]$IncludeHistory,
    [switch]$Overwrite,
    [string]$ExpectedUserSid,
    [string[]]$BackupPaths
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($RootPath).TrimEnd([char[]]'\')
$data = Join-Path $root 'Data'
$utf8 = New-Object Text.UTF8Encoding($false)
$keys = [ordered]@{
    PotPlayer = 'HKCU\Software\Daum\PotPlayerMini64'
    LAV = 'HKCU\Software\LAV'
    madVR = 'HKCU\Software\madshi\madVR'
    VSFilter = 'HKCU\Software\Gabest\VSFilter'
    XySubFilter = 'HKCU\Software\Gabest\XySubFilter'
}
function Export-Key($key, $file) {
    & reg.exe export $key $file /y | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "配置导出失败：$key" }
}
function Check-Files {
    foreach ($file in @('PotPlayerPortable.exe', 'x64\PotPlayerMini64.exe', 'x64\XySubFilter.dll', 'x64\LAVFilters64\LAVSplitter.ax', 'x64\LAVFilters64\LAVVideo.ax', 'x64\LAVFilters64\LAVAudio.ax', 'madVR\madVR64.ax', 'madVR\madHcCtrl.exe', 'madVR\settings.bin', 'Data\madVR-画质优先.reg', 'Icaros\IcarosConfig.exe', 'Data\SourcePath.txt', 'Data\PotPlayer.reg', 'Data\madVR.reg')) {
        if (!(Test-Path -LiteralPath (Join-Path $root $file))) { throw "缺少文件：$file" }
    }
}
function New-DesktopShortcut {
    $file = Join-Path $root 'PotPlayerPortable.exe'
    if (!(Test-Path -LiteralPath $file)) { throw '缺少 PotPlayer 播放器文件。' }
    $desktop = [Environment]::GetFolderPath('DesktopDirectory')
    $path = Join-Path $desktop 'PotPlayer 便携版.lnk'
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($path)
    $description = '启动便携版 PotPlayer'
    if ((Test-Path -LiteralPath $path) -and $shortcut.TargetPath -ne $file -and $shortcut.Description -ne $description) {
        throw '桌面已存在其他程序的同名快捷方式，请先将其改名。'
    }
    $shortcut.TargetPath = $file
    $shortcut.WorkingDirectory = Split-Path -Parent $file
    $shortcut.IconLocation = (Join-Path $root 'x64\PotPlayerMini64.exe') + ',0'
    $shortcut.Description = $description
    $shortcut.Save()
    Write-Output '桌面快捷方式已创建：' $path
    Write-Output '双击直接启动 PotPlayer。移动便携目录后，再点击“创建桌面快捷方式” 更新快捷方式。'
}
function Register-VideoAssociations([switch]$NoSettings) {
    $file = Join-Path $root 'PotPlayerPortable.exe'
    if (!(Test-Path -LiteralPath $file)) { throw '缺少 PotPlayer 播放器文件。' }
    $extensions = @('.mp4', '.mkv', '.avi', '.mov', '.wmv', '.flv', '.webm', '.m4v', '.ts', '.m2ts', '.rmvb')
    $name = 'PotPlayer 便携版'
    $capabilities = 'Software\PotPlayerPortable\Capabilities'
    $application = 'Software\Classes\Applications\PotPlayerMini64.exe'
    $backup = Join-Path $root ('Backups\文件关联-' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss-fff'))
    New-Item -ItemType Directory -Path $backup | Out-Null
    $paths = @($capabilities, $application)
    foreach ($extension in $extensions) {
        $paths += 'Software\Classes\PotPlayerMini64' + $extension
        $paths += 'Software\Classes\' + $extension + '\OpenWithProgids'
    }
    $records = @()
    foreach ($path in $paths) {
        $exists = Test-Path -LiteralPath ('Registry::HKEY_CURRENT_USER\' + $path)
        $export = $records.Count.ToString() + '.reg'
        if ($exists) { Export-Key ('HKCU\' + $path) (Join-Path $backup $export) }
        $records += [PSCustomObject]@{Key = $path; Existed = $exists; Export = $export}
    }
    $registered = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\RegisteredApplications')
    $oldRegistration = $null
    if ($registered) {
        try { $oldRegistration = $registered.GetValue($name) } finally { $registered.Dispose() }
    }
    $snapshot = [ordered]@{Keys = $records; ApplicationName = $name; PreviousRegistration = $oldRegistration}
    [IO.File]::WriteAllText((Join-Path $backup '原关联登记.json'), ($snapshot | ConvertTo-Json -Depth 4), $utf8)
    function Set-AssociationValue($path, $valueName, $value, $kind = [Microsoft.Win32.RegistryValueKind]::String) {
        $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($path)
        try { $key.SetValue($valueName, $value, $kind) } finally { $key.Dispose() }
    }
    $command = '"' + $file + '" "%1"'
    $icon = '"' + (Join-Path $root 'x64\PotPlayerMini64.exe') + '",0'
    foreach ($extension in $extensions) {
        # 沿用安装版的 ProgID，恢复卸载后仍指向它的默认打开方式。
        $progId = 'PotPlayerMini64' + $extension
        $class = 'Software\Classes\' + $progId
        Set-AssociationValue $class '' ('PotPlayer 视频 (' + $extension + ')')
        Set-AssociationValue ($class + '\DefaultIcon') '' $icon
        Set-AssociationValue ($class + '\shell') '' 'open'
        Set-AssociationValue ($class + '\shell\open\command') '' $command
        # 清空当前用户的 CLSID，避免系统级 DropTarget 绕过便携启动器。
        Set-AssociationValue ($class + '\shell\open\DropTarget') 'Clsid' ''
        Set-AssociationValue ($capabilities + '\FileAssociations') $extension $progId
        Set-AssociationValue ($application + '\SupportedTypes') $extension ''
        Set-AssociationValue ('Software\Classes\' + $extension + '\OpenWithProgids') $progId ([byte[]]@()) ([Microsoft.Win32.RegistryValueKind]::None)
    }
    Set-AssociationValue $capabilities 'ApplicationName' $name
    Set-AssociationValue $capabilities 'ApplicationDescription' '便携版 PotPlayer 视频播放器（madVR）'
    Set-AssociationValue $capabilities 'ApplicationIcon' $icon
    Set-AssociationValue $application 'FriendlyAppName' $name
    Set-AssociationValue ($application + '\shell\open\command') '' $command
    Set-AssociationValue 'Software\RegisteredApplications' $name $capabilities
    if (!('PotPlayerPortable.ShellNotify' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace PotPlayerPortable {
    public static class ShellNotify {
        [DllImport("shell32.dll")]
        public static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
    }
}
'@
    }
    [PotPlayerPortable.ShellNotify]::SHChangeNotify(0x08000000, 0x1000, [IntPtr]::Zero, [IntPtr]::Zero)
    Write-Output '常见视频格式的打开方式已登记，原 PotPlayer 关联的命令已更新为本目录。'
    Write-Output '原登记备份位于：' $backup
    Write-Output '请在 Windows 默认应用中选择 PotPlayer 便携版；已选择其他软件的格式需要在此更改。'
    Write-Output '可在“按应用设置默认值”中找到本播放器；也可右键视频选择“打开方式”，勾选始终使用。'
    Write-Output '移动目录或换电脑后，再点击“注册视频关联” 更新文件关联。'
    if (!$NoSettings) { Start-Process 'ms-settings:defaultapps' }
}

function Ensure-Closed {
    if (Get-Process -Name PotPlayerMini64, PotPlayerPortable, IcarosConfig, madHcCtrl -ErrorAction SilentlyContinue) {
        throw '请先关闭 PotPlayer、按键工具、Icaros 配置和 madVR 托盘工具。'
    }
}
function Write-Utf8($path, $text) { [IO.File]::WriteAllText($path, $text, $utf8) }
function Write-Reg($path, $text) { [IO.File]::WriteAllText($path, $text, [Text.Encoding]::Unicode) }
function Assert-Admin {
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw '此操作需要管理员权限。请在管理工具中允许系统提权。' }
}
function Resolve-ConfigFolder($path) {
    $path = [IO.Path]::GetFullPath($path)
    if ([IO.File]::Exists($path)) {
        if ([IO.Path]::GetExtension($path) -ine '.zip') { throw '请选择配置 ZIP 或包含配置的文件夹。' }
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $temp = New-TemporaryFolder
        $zip = [IO.Compression.ZipFile]::OpenRead($path)
        try {
            $names = @('PotPlayer.reg','LAV.reg','madVR.reg','VSFilter.reg','XySubFilter.reg','Icaros-machine.reg','SourcePath.txt','manifest.json')
            $entries = @($zip.Entries | Where-Object { [IO.Path]::GetFileName($_.FullName) -in $names })
            $folders = @($entries | Where-Object { $_.Name -eq 'PotPlayer.reg' -or $_.Name -eq 'madVR.reg' } | ForEach-Object { $_.FullName.Substring(0,$_.FullName.Length-$_.Name.Length) } | Select-Object -Unique)
            if ($folders.Count -ne 1) { throw '压缩包配置位置不明确，或没有 PotPlayer/madVR 配置。' }
            foreach ($entry in $entries | Where-Object { $_.FullName -eq ($folders[0]+$_.Name) }) {
                if ($entry.Length -gt 16MB) { throw '配置文件过大：' + $entry.Name }
                $destination = Join-Path $temp $entry.Name
                if (Test-Path -LiteralPath $destination) { throw '压缩包中存在重复配置文件。' }
                [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,$destination,$false)
            }
        } finally { $zip.Dispose() }
        return $temp
    }
    if (!(Test-Path -LiteralPath $path -PathType Container)) { throw '配置目录不存在。' }
    foreach ($name in $keys.Keys) { if (Test-Path -LiteralPath (Join-Path $path ($name+'.reg'))) { return $path } }
    if (Test-Path -LiteralPath (Join-Path $path 'Data\PotPlayer.reg')) { return Join-Path $path 'Data' }
    return $path
}
function Assert-Reg($text, $name) {
    $expected = if ($name -eq 'Icaros-machine') { 'HKEY_LOCAL_MACHINE\SOFTWARE\Icaros' } else { $keys[$name].Replace('HKCU\','HKEY_CURRENT_USER\') }
    if (!$expected -or !$text.StartsWith('Windows Registry Editor Version 5.00')) { throw '配置文件格式不支持：' + $name }
    $matches = [regex]::Matches($text,'(?m)^\[(-?[^\]\r\n]+)\]')
    if (!$matches.Count) { throw '配置文件为空：' + $name }
    foreach ($match in $matches) {
        $key = $match.Groups[1].Value
        if ($key -ine $expected -and !$key.StartsWith($expected+'\',[StringComparison]::OrdinalIgnoreCase)) { throw '配置包含非本组件的注册表项：' + $name }
    }
}
function Filter-Reg($text, [switch]$Portable, [switch]$ProtectedOnly) {
    $output = New-Object Text.StringBuilder
    [void]$output.Append("Windows Registry Editor Version 5.00`r`n`r`n")
    foreach ($section in [regex]::Split($text,'(?m)(?=^\[HKEY_)') | Select-Object -Skip 1) {
        $header = ($section -split '\r?\n',2)[0]
        $protectedKey = $header -match '(?i)\\_Url(?:Cookie|Header|Referer|UserAgent)(?:\\|\])'
        $record = $header -match '(?i)\\(?:BMItem_\d+|RecentFiles|RecentURLs|FileList|URLList|FilePosition|FilePositions|PlayHistory|PlayList)(?:\\|\])'
        $integrationBackup = $header -match '(?i)\\Icaros\\(?:ThumbBackup|PropBackup)(?:\\|\])'
        if ($protectedKey) {
            if ($ProtectedOnly) { [void]$output.Append($section) }
            continue
        }
        if ($Portable -and ($record -or $integrationBackup)) { continue }
        $values = New-Object Text.StringBuilder
        foreach ($value in [regex]::Split($section,'(?m)(?=^(?:"(?:\\.|[^"\r\n])*"|@)=)') | Select-Object -Skip 1) {
            $name = ([regex]::Match($value,'^"((?:\\.|[^"\r\n])*)"=')).Groups[1].Value
            $protected = $name -match '(?i)^(?:Password|Token|Secret|AccessToken|RefreshToken|ApiKey|Cookie|Authorization|MInfo1|MInfo2|ServiceCount|ServiceValue)$'
            $history = $name -match '(?i)^(?:RecentFile\d*|RecentURL\d*|LastFileName|LastOpenFile|LastPlayFile|LastOpenFolder|LastPlayFolder|PrevSpeed)$'
            if ($ProtectedOnly) { if ($protected) { [void]$values.Append($value) } }
            elseif (!$protected -and !($Portable -and $history)) { [void]$values.Append($value) }
        }
        if (!$ProtectedOnly -or $values.Length) {
            [void]$output.Append($header+"`r`n"+$values.ToString()+"`r`n")
        }
    }
    return $output.ToString()
}
function Read-Snapshot($path) {
    $folder = Resolve-ConfigFolder $path
    $manifestFile = Join-Path $folder 'manifest.json'
    $manifest = if (Test-Path -LiteralPath $manifestFile) { [IO.File]::ReadAllText($manifestFile) | ConvertFrom-Json } else { $null }
    if ($manifest -and $manifest.Format -ne 'PotPlayerPortableConfig') { throw '此配置清单格式不支持。' }
    if ($manifest -and $manifest.Version -ne 1) { throw '此配置包版本不支持。' }
    $contents = [ordered]@{}
    foreach ($name in @($keys.Keys)+@('Icaros-machine')) {
        $file = Join-Path $folder ($name+'.reg')
        if (!(Test-Path -LiteralPath $file)) { continue }
        if ((Get-Item -LiteralPath $file).Length -gt 16MB) { throw '配置文件过大：' + $name }
        $text = [IO.File]::ReadAllText($file)
        Assert-Reg $text $name
        $contents[$name] = $text
    }
    if (!$contents.Count) { throw '所选位置没有可恢复的播放器配置。' }
    if ($manifest -and $manifest.Files) {
        foreach ($item in $manifest.Files) {
            if ($item.Name -notin @($keys.Keys | ForEach-Object { $_+'.reg' })+@('Icaros-machine.reg','SourcePath.txt')) { throw '配置清单包含未知文件。' }
            $file = Join-Path $folder $item.Name
            if (!(Test-Path -LiteralPath $file) -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ine $item.Sha256) { throw '配置完整性检查失败：' + $item.Name }
        }
    }
    $sourceFile = Join-Path $folder 'SourcePath.txt'
    $source = if (Test-Path -LiteralPath $sourceFile) { [IO.File]::ReadAllText($sourceFile).Trim() } elseif ($manifest) { $manifest.SourceRoot } else { '' }
    if (!$source -and $contents.Contains('PotPlayer')) {
        $match = [regex]::Match($contents['PotPlayer'],'(?im)^"Path"="(.+?)\\\\x64\\\\LAVFilters64\\\\LAV(?:Video|Audio|Splitter)\.ax"')
        if ($match.Success) { $source = $match.Groups[1].Value.Replace('\\','\') }
    }
    if ($contents.Contains('PotPlayer') -and !$source -and $contents['PotPlayer'] -match '(?im)^"Path"="[A-Z]:') { throw '旧配置缺少来源路径，无法可靠迁移解码器路径。' }
    return [PSCustomObject]@{Folder=$folder;Contents=$contents;SourceRoot=$source;Manifest=$manifest}
}
function Convert-Paths($text, $source) {
    if ($source) { $text = $text.Replace($source.TrimEnd('\').Replace('\','\\'),$root.Replace('\','\\')) }
    return $text
}
function Write-Manifest($folder, $kind, $snapshot=$null) {
    $files = @(Get-ChildItem -LiteralPath $folder -File | Where-Object { $_.Extension -eq '.reg' -or $_.Name -eq 'SourcePath.txt' } | ForEach-Object {
        [ordered]@{Name=$_.Name;Sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
    })
    $manifest = [ordered]@{Format='PotPlayerPortableConfig';Version=1;Kind=$kind;Created=[DateTimeOffset]::Now.ToString('o');SourceRoot=$root;Files=$files}
    if ($snapshot) {
        $reference = Get-ChildItem -LiteralPath $snapshot.Folder -File -Filter '*.reg' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        $manifest['ConfigSavedAt'] = if ($snapshot.Manifest -and $snapshot.Manifest.ConfigSavedAt) { $snapshot.Manifest.ConfigSavedAt } else { $reference.LastWriteTime.ToString('o') }
        $manifest['SourceSnapshot'] = [IO.Path]::GetFileName($snapshot.Folder)
    }
    Write-Utf8 (Join-Path $folder 'manifest.json') ($manifest | ConvertTo-Json -Depth 5)
}
function New-TemporaryFolder {
    $folder = Join-Path ([IO.Path]::GetTempPath()) ('PotPlayerManager-'+[Guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($folder)
    $script:temporary += $folder
    return $folder
}
function Sync-MadVR($text) {
    $match = [regex]::Match($text,'(?im)^"Settings"=hex:([0-9a-f,\\\s]+?)(?=^"|^\[|\z)')
    if (!$match.Success) { throw 'madVR 配置没有有效的 Settings 数据。' }
    $hex = $match.Groups[1].Value -replace '[,\\\s]',''
    if ($hex.Length % 2) { throw 'madVR 配置数据长度不正确。' }
    $bytes = New-Object byte[] ($hex.Length/2)
    for ($i=0;$i -lt $bytes.Length;$i++) { $bytes[$i]=[Convert]::ToByte($hex.Substring($i*2,2),16) }
    [IO.File]::WriteAllBytes((Join-Path $root 'madVR\settings.bin'),$bytes)
}
function Write-Location {
    Write-Utf8 (Join-Path $data 'ActiveLocation.json') ([ordered]@{Computer=$env:COMPUTERNAME;UserSid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value;Path=$root} | ConvertTo-Json)
}
function Save-Current([switch]$RequireLocation) {
    Ensure-Closed
    if ($RequireLocation) {
        $file = Join-Path $data 'ActiveLocation.json'
        if (!(Test-Path -LiteralPath $file)) { throw '请先初始化本目录，再保存当前配置。' }
        $active = [IO.File]::ReadAllText($file) | ConvertFrom-Json
        if ($active.Path -ne $root -or $active.Computer -ne $env:COMPUTERNAME -or $active.UserSid -ne [Security.Principal.WindowsIdentity]::GetCurrent().User.Value) { throw '电脑、用户或目录已改变，请先初始化。' }
    }
    $stage = Join-Path $data ('Snapshot-'+[DateTime]::Now.ToString('yyyyMMdd-HHmmss-fff'))
    [void][IO.Directory]::CreateDirectory($stage)
    foreach ($entry in $keys.GetEnumerator()) {
        if (Test-Path -LiteralPath ($entry.Value.Replace('HKCU\','Registry::HKEY_CURRENT_USER\'))) { Export-Key $entry.Value (Join-Path $stage ($entry.Key+'.reg')) }
    }
    if (!(Test-Path -LiteralPath (Join-Path $stage 'PotPlayer.reg')) -or !(Test-Path -LiteralPath (Join-Path $stage 'madVR.reg'))) { throw '播放器或 madVR 配置缺失，请先初始化。' }
    if (Test-Path -LiteralPath 'HKLM:\SOFTWARE\Icaros') { Export-Key 'HKLM\SOFTWARE\Icaros' (Join-Path $stage 'Icaros-machine.reg') }
    Write-Utf8 (Join-Path $stage 'SourcePath.txt') $root
    Write-Manifest $stage 'Saved'
    Sync-MadVR ([IO.File]::ReadAllText((Join-Path $stage 'madVR.reg')))
    foreach ($file in Get-ChildItem -LiteralPath $stage -File) { Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $data $file.Name) -Force }
    Write-Location
    Write-Output ('当前配置已保存。快照：'+$stage)
}
function New-ConfigBackup($label) {
    $backup = Join-Path $root ('Backups\'+$label+'-'+[DateTime]::Now.ToString('yyyyMMdd-HHmmss-fff'))
    [void][IO.Directory]::CreateDirectory($backup)
    $existed = [ordered]@{}
    foreach ($entry in $keys.GetEnumerator()) {
        $existed[$entry.Key] = Test-Path -LiteralPath ($entry.Value.Replace('HKCU\','Registry::HKEY_CURRENT_USER\'))
        if ($existed[$entry.Key]) { Export-Key $entry.Value (Join-Path $backup ($entry.Key+'.reg')) }
    }
    $existed['Icaros-machine'] = Test-Path -LiteralPath 'HKLM:\SOFTWARE\Icaros'
    $existed['Playlist'] = Test-Path -LiteralPath (Join-Path $root 'x64\Playlist')
    if ($IncludeIcaros -and $existed['Icaros-machine']) { Export-Key 'HKLM\SOFTWARE\Icaros' (Join-Path $backup 'Icaros-machine.reg') }
    $saved = Join-Path $backup 'Data'
    [void][IO.Directory]::CreateDirectory($saved)
    foreach ($file in Get-ChildItem -LiteralPath $data -File) { Copy-Item -LiteralPath $file.FullName -Destination $saved }
    Copy-Item -LiteralPath (Join-Path $root 'madVR\settings.bin') -Destination $backup
    if ($IncludeHistory -and (Test-Path -LiteralPath (Join-Path $root 'x64\Playlist'))) { Copy-Item -LiteralPath (Join-Path $root 'x64\Playlist') -Destination $backup -Recurse }
    Write-Utf8 (Join-Path $backup 'SourcePath.txt') $root
    Write-Utf8 (Join-Path $backup '还原前状态.json') ($existed | ConvertTo-Json)
    return $backup
}
function Delete-KnownKey($key) {
    if ($key -notin @($keys.Values)+@('HKLM\SOFTWARE\Icaros')) { throw '禁止处理非本工具的配置键。' }
    & reg.exe delete $key /f 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw '配置键重置失败：' + $key }
}
function Import-Reg($file) {
    & reg.exe import $file | Out-Null
    if ($LASTEXITCODE -ne 0) { throw '配置导入失败：' + [IO.Path]::GetFileName($file) }
}
function Rollback-Config($backup) {
    $before = [IO.File]::ReadAllText((Join-Path $backup '还原前状态.json')) | ConvertFrom-Json
    foreach ($entry in $keys.GetEnumerator()) {
        if (Test-Path -LiteralPath ($entry.Value.Replace('HKCU\','Registry::HKEY_CURRENT_USER\'))) { Delete-KnownKey $entry.Value }
        if ($before.($entry.Key)) { Import-Reg (Join-Path $backup ($entry.Key+'.reg')) }
    }
    if ($IncludeIcaros) {
        if (Test-Path -LiteralPath 'HKLM:\SOFTWARE\Icaros') { Delete-KnownKey 'HKLM\SOFTWARE\Icaros' }
        if ($before.'Icaros-machine') { Import-Reg (Join-Path $backup 'Icaros-machine.reg') }
    }
    foreach ($file in Get-ChildItem -LiteralPath $data -File) {
        if (!(Test-Path -LiteralPath (Join-Path $backup ('Data\'+$file.Name)))) { Remove-Item -LiteralPath $file.FullName }
    }
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $backup 'Data') -File) { Copy-Item -LiteralPath $file.FullName -Destination $data -Force }
    Copy-Item -LiteralPath (Join-Path $backup 'settings.bin') -Destination (Join-Path $root 'madVR\settings.bin') -Force
    if ($IncludeHistory) {
        if ($before.Playlist) { Restore-Playlist (Join-Path $backup 'Playlist') }
        else { Restore-Playlist $null }
    }
}
function Restore-Playlist($source) {
    $destination = [IO.Path]::GetFullPath((Join-Path $root 'x64\Playlist'))
    if ($destination -ne ([IO.Path]::GetFullPath($root)+'\x64\Playlist')) { throw '播放列表目标不正确。' }
    if (Test-Path -LiteralPath $destination) { Remove-Item -LiteralPath $destination -Recurse -Force }
    if ($source) { Copy-Item -LiteralPath $source -Destination $destination -Recurse }
}
function Apply-Snapshot($path) {
    Ensure-Closed
    if ($IncludeIcaros) { Assert-Admin }
    if ($Action -eq 'Quality' -and !(Test-Path -LiteralPath 'HKCU:\Software\Daum\PotPlayerMini64\Settings')) { throw '播放器尚未初始化，请先初始化本目录。' }
    $snapshot = Read-Snapshot $path
    $staging = New-TemporaryFolder
    foreach ($entry in $snapshot.Contents.GetEnumerator()) {
        $text = Filter-Reg (Convert-Paths $entry.Value $snapshot.SourceRoot)
        Write-Reg (Join-Path $staging ($entry.Key+'.reg')) $text
        if ($entry.Key -eq 'madVR') {
            $check = [regex]::Match($text,'(?im)^"Settings"=hex:')
            if (!$check.Success) { throw 'madVR 配置缺少 Settings 数据。' }
        }
    }
    $backup = New-ConfigBackup '还原前'
    Write-Output ('当前状态已备份：'+$backup)
    try {
        foreach ($entry in $keys.GetEnumerator()) {
            $file = Join-Path $staging ($entry.Key+'.reg')
            if (!(Test-Path -LiteralPath $file)) { continue }
            $old = Join-Path $backup ($entry.Key+'.reg')
            $protectedFile = Join-Path $staging ($entry.Key+'.protected.reg')
            if (Test-Path -LiteralPath $old) {
                $protectedText = Filter-Reg ([IO.File]::ReadAllText($old)) -ProtectedOnly
                if ($protectedText -match '(?m)^\[HKEY_') { Write-Reg $protectedFile $protectedText }
            }
            if (Test-Path -LiteralPath ($entry.Value.Replace('HKCU\','Registry::HKEY_CURRENT_USER\'))) { Delete-KnownKey $entry.Value }
            Import-Reg $file
            if (Test-Path -LiteralPath $protectedFile) { Import-Reg $protectedFile }
        }
        if ($IncludeIcaros -and $snapshot.Contents.Contains('Icaros-machine')) {
            Write-Reg (Join-Path $staging 'Icaros-machine.reg') (Filter-Reg (Convert-Paths $snapshot.Contents['Icaros-machine'] $snapshot.SourceRoot) -Portable)
            Import-Reg (Join-Path $staging 'Icaros-machine.reg')
        }
        if ($IncludeHistory -and (Test-Path -LiteralPath (Join-Path $snapshot.Folder 'Playlist'))) { Restore-Playlist (Join-Path $snapshot.Folder 'Playlist') }
        if ($Action -eq 'Quality') { Set-ItemProperty -LiteralPath 'HKCU:\Software\Daum\PotPlayerMini64\Settings' -Name VideoRen2 -Value 10 }
        Save-Current
        Write-Output '配置已覆盖应用并保存；组件路径已适配当前目录。'
    } catch {
        $failure = $_.Exception.Message
        try { Rollback-Config $backup } catch { throw ($failure+'；自动恢复失败，请使用备份：'+$backup+'；'+$_.Exception.Message) }
        throw ($failure+'；已恢复操作前状态。')
    }
}
function Assert-AssociationPath($path) {
    $extensions = @('mp4','mkv','avi','mov','wmv','flv','webm','m4v','ts','m2ts','rmvb') -join '|'
    if ($path -notmatch ('^Software\\(?:PotPlayerPortable\\Capabilities|Classes\\Applications\\PotPlayerMini64\.exe|Classes\\PotPlayerMini64\.(?:'+$extensions+')|Classes\\\.(?:'+$extensions+')\\OpenWithProgids)$')) { throw '关联备份含有非本工具管理的键。' }
}
function Apply-AssociationRecord($folder) {
    $record = [IO.File]::ReadAllText((Join-Path $folder '原关联登记.json')) | ConvertFrom-Json
    if ($record.ApplicationName -ne 'PotPlayer 便携版') { throw '关联备份的应用名称不匹配。' }
    foreach ($item in $record.Keys) {
        Assert-AssociationPath $item.Key
        if ($item.Export -notmatch '^\d+\.reg$') { throw '关联备份文件名不正确。' }
        $file = Join-Path $folder $item.Export
        if ($item.Existed) {
            if (!(Test-Path -LiteralPath $file)) { throw '关联备份文件缺失。' }
            foreach ($match in [regex]::Matches([IO.File]::ReadAllText($file),'(?m)^\[([^\]]+)\]')) {
                $expected = 'HKEY_CURRENT_USER\'+$item.Key
                if ($match.Groups[1].Value -ine $expected -and !$match.Groups[1].Value.StartsWith($expected+'\',[StringComparison]::OrdinalIgnoreCase)) { throw '关联备份范围不一致。' }
            }
        }
    }
    foreach ($item in $record.Keys) {
        $key = 'Registry::HKEY_CURRENT_USER\'+$item.Key
        if (Test-Path -LiteralPath $key) { Remove-Item -LiteralPath $key -Recurse }
        if ($item.Existed) { Import-Reg (Join-Path $folder $item.Export) }
    }
    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Software\RegisteredApplications')
    try {
        if ($null -eq $record.PreviousRegistration) { $key.DeleteValue($record.ApplicationName,$false) }
        else { $key.SetValue($record.ApplicationName,$record.PreviousRegistration) }
    } finally { $key.Dispose() }
}
function Restore-Associations($folder) {
    Ensure-Closed
    $record = [IO.File]::ReadAllText((Join-Path $folder '原关联登记.json')) | ConvertFrom-Json
    $backup = Join-Path $root ('Backups\关联还原前-'+[DateTime]::Now.ToString('yyyyMMdd-HHmmss-fff'))
    [void][IO.Directory]::CreateDirectory($backup)
    $records = @()
    foreach ($item in $record.Keys) {
        Assert-AssociationPath $item.Key
        if ($item.Export -notmatch '^\d+\.reg$') { throw '关联备份文件名不正确。' }
        $exists = Test-Path -LiteralPath ('Registry::HKEY_CURRENT_USER\'+$item.Key)
        if ($exists) { Export-Key ('HKCU\'+$item.Key) (Join-Path $backup $item.Export) }
        $records += [ordered]@{Key=$item.Key;Existed=$exists;Export=$item.Export}
    }
    if (!('PotPlayerPortable.ShellNotify' -as [type])) {
        Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; namespace PotPlayerPortable { public static class ShellNotify { [DllImport("shell32.dll")] public static extern void SHChangeNotify(int e,uint f,IntPtr a,IntPtr b); } }'
    }
    [PotPlayerPortable.ShellNotify]::SHChangeNotify(0x08000000,0x1000,[IntPtr]::Zero,[IntPtr]::Zero)
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\RegisteredApplications')
    $value = if ($key) { try { $key.GetValue('PotPlayer 便携版') } finally { $key.Dispose() } } else { $null }
    Write-Utf8 (Join-Path $backup '原关联登记.json') ([ordered]@{Keys=$records;ApplicationName='PotPlayer 便携版';PreviousRegistration=$value} | ConvertTo-Json -Depth 5)
    try { Apply-AssociationRecord $folder } catch {
        $failure = $_.Exception.Message
        try { Apply-AssociationRecord $backup } catch { throw ($failure+'；自动恢复失败，备份：'+$backup) }
        throw ($failure+'；已恢复操作前的关联登记。')
    }
    Write-Output ('关联登记已恢复；Windows UserChoice 不改动。操作前备份：'+$backup)
}
function Export-Package {
    Ensure-Closed
    $snapshot = Read-Snapshot $SourcePath
    if ($PackageKind -eq 'Portable' -and (!$snapshot.Contents.Contains('PotPlayer') -or !$snapshot.Contents.Contains('madVR'))) { throw '完整便携包需要同时包含 PotPlayer 和 madVR 配置。请选择完整快照。' }
    $destination = [IO.Path]::GetFullPath($DestinationPath)
    if ($destination -eq $root -or $destination.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)) { throw '请选择本便携目录之外的导出位置，避免导出包被再次打包。' }
    if ($Format -eq 'Directory' -and (Test-Path -LiteralPath $destination)) { throw '导出文件夹已存在，请选择新的目录。' }
    if ($Format -eq 'Zip' -and (Test-Path -LiteralPath $destination) -and !$Overwrite) { throw 'ZIP 已存在，请确认覆盖或选择新文件名。' }
    $stage = New-TemporaryFolder
    $name = if ($PackageKind -eq 'Portable') { 'PotPlayer-Portable' } else { 'PotPlayer-Config' }
    $package = Join-Path $stage $name
    [void][IO.Directory]::CreateDirectory($package)
    if ($PackageKind -eq 'Portable') {
        Write-Output '正在复制运行组件，排除旧备份、配置快照和播放记录……'
        foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File) {
            $relative = $file.FullName.Substring($root.Length+1)
            if ($relative -match '^(?:Data|Backups|Exports|work)(?:\\|$)' -or $relative -match '(?i)(?:^|\\)(?:Playlist|Capture|Cache|Logs|Temp|\.git)(?:\\|$)' -or $relative -match '(?i)\.(?:dpl|pbf|log|tmp|bak|dmp|zip)$') { continue }
            $target = Join-Path $package $relative
            [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))
            Copy-Item -LiteralPath $file.FullName -Destination $target
        }
    }
    $config = if ($PackageKind -eq 'Portable') { Join-Path $package 'Data' } else { $package }
    [void][IO.Directory]::CreateDirectory($config)
    foreach ($entry in $snapshot.Contents.GetEnumerator()) {
        Write-Reg (Join-Path $config ($entry.Key+'.reg')) (Filter-Reg (Convert-Paths $entry.Value $snapshot.SourceRoot) -Portable)
    }
    Write-Utf8 (Join-Path $config 'SourcePath.txt') $root
    Write-Manifest $config 'Exported' $snapshot
    if ($PackageKind -eq 'Portable') {
        $preset = [IO.File]::ReadAllText((Join-Path $data 'madVR-画质优先.reg'))
        Write-Reg (Join-Path $config 'madVR-画质优先.reg') (Filter-Reg $preset -Portable)
        $match = [regex]::Match([IO.File]::ReadAllText((Join-Path $config 'madVR.reg')),'(?im)^"Settings"=hex:([0-9a-f,\\\s]+?)(?=^"|^\[|\z)')
        if (!$match.Success) { throw '导出的 madVR 配置缺少 Settings 数据。' }
        $hex = $match.Groups[1].Value -replace '[,\\\s]',''
        $bytes = New-Object byte[] ($hex.Length/2)
        for ($i=0;$i -lt $bytes.Length;$i++) { $bytes[$i]=[Convert]::ToByte($hex.Substring($i*2,2),16) }
        [IO.File]::WriteAllBytes((Join-Path $package 'madVR\settings.bin'),$bytes)
    }
    [void](Read-Snapshot $config)
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination))
    if ($Format -eq 'Zip') {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        Write-Output '正在生成 ZIP 压缩包……'
        $archive = Join-Path $stage 'export.zip'
        [IO.Compression.ZipFile]::CreateFromDirectory($package,$archive,[IO.Compression.CompressionLevel]::Optimal,$true)
        $pending = $destination+'.partial-'+[Guid]::NewGuid().ToString('N')
        try {
            Copy-Item -LiteralPath $archive -Destination $pending
            if ([IO.File]::Exists($destination)) { [IO.File]::Replace($pending,$destination,$null) }
            else { [IO.File]::Move($pending,$destination) }
        } finally { if ([IO.File]::Exists($pending)) { [IO.File]::Delete($pending) } }
    } else { Copy-Item -LiteralPath $package -Destination $destination -Recurse }
    Write-Output ('导出完成：'+$destination)
}
function Remove-Backups {
    if (!$BackupPaths -or !$BackupPaths.Count) { throw '请先选择要删除的备份。' }
    $parents = @((Join-Path $root 'Backups'),$data)
    $targets = @($BackupPaths | Select-Object -Unique | ForEach-Object {
        $path = [IO.Path]::GetFullPath($_).TrimEnd('\')
        $parent = [IO.Path]::GetDirectoryName($path)
        if ($parent -notin $parents -or ($parent -eq $data -and [IO.Path]::GetFileName($path) -notlike 'Snapshot-*')) { throw '只能删除列表中的备份或配置快照：'+$path }
        $item = Get-Item -LiteralPath $path
        $parentItem = Get-Item -LiteralPath $parent
        if (!$item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or ($parentItem.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw '备份目录不正确或属于目录链接：'+$path }
        $path
    })
    Add-Type -AssemblyName Microsoft.VisualBasic
    foreach ($path in $targets) {
        [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteDirectory($path,[Microsoft.VisualBasic.FileIO.UIOption]::OnlyErrorDialogs,[Microsoft.VisualBasic.FileIO.RecycleOption]::SendToRecycleBin,[Microsoft.VisualBasic.FileIO.UICancelOption]::ThrowException)
        Write-Output ('已移入回收站：'+[IO.Path]::GetFileName($path))
    }
}
function Register-Components {
    Ensure-Closed
    Assert-Admin
    foreach ($file in @('madVR\madVR64.ax','x64\XySubFilter.dll')) {
        $process = Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\regsvr32.exe') -ArgumentList @('/s',('"'+(Join-Path $root $file)+'"')) -WindowStyle Hidden -PassThru -Wait
        if ($process.ExitCode -ne 0) { throw '组件注册失败：'+$file }
    }
    if (Test-Path -LiteralPath (Join-Path $data 'Icaros-machine.reg')) {
        $stage = New-TemporaryFolder
        $text = [IO.File]::ReadAllText((Join-Path $data 'Icaros-machine.reg'))
        Assert-Reg $text 'Icaros-machine'
        Write-Reg (Join-Path $stage 'Icaros-machine.reg') (Filter-Reg $text -Portable)
        Import-Reg (Join-Path $stage 'Icaros-machine.reg')
    }
    Write-Output 'madVR 和字幕组件已注册。Icaros 缩略图请在其配置窗口激活。'
}
$script:temporary = @()
$lock = $null
$locked = $false
$exitCode = 0
try {
    $hash = [Security.Cryptography.SHA256]::Create()
    try { $lockName = [BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($root.ToLowerInvariant()))).Replace('-','') } finally { $hash.Dispose() }
    $lock = New-Object Threading.Mutex($false,('Local\PotPlayerManagerOperation-'+$lockName))
    try { $locked = $lock.WaitOne(0) } catch [Threading.AbandonedMutexException] { $locked = $true }
    if (!$locked) { throw '另一个配置操作正在执行，请等待完成。' }
    if ($ExpectedUserSid -and $Action -in @('Init','Save','Quality','Import','Restore') -and [Security.Principal.WindowsIdentity]::GetCurrent().User.Value -ne $ExpectedUserSid) { throw '提权账户与当前用户不同。请用原用户导入播放器配置，系统组件另行注册。' }
    if ($Action -notin @('Preview','DeleteBackups')) { Check-Files }
    switch ($Action) {
        'Check' { Write-Output '组件文件检查通过。' }
        'Init' { Apply-Snapshot $data }
        'Save' { Save-Current -RequireLocation }
        'Register' { Register-Components }
        'Shortcut' { New-DesktopShortcut }
        'Associations' { Register-VideoAssociations }
        'Quality' {
            Ensure-Closed
            $temp = New-TemporaryFolder
            Copy-Item -LiteralPath (Join-Path $data 'madVR-画质优先.reg') -Destination (Join-Path $temp 'madVR.reg')
            Write-Utf8 (Join-Path $temp 'SourcePath.txt') $root
            Apply-Snapshot $temp
            Write-Output '已应用固定 madVR 画质预设；当前自定义配置已提前备份。'
        }
        'Restore' {
            if (Test-Path -LiteralPath (Join-Path $SourcePath '原关联登记.json')) { Restore-Associations $SourcePath }
            else { Apply-Snapshot $SourcePath }
        }
        'Import' { Apply-Snapshot $SourcePath }
        'Preview' {
            $snapshot = Read-Snapshot $SourcePath
            Write-Output ('包含组件：'+($snapshot.Contents.Keys -join '、'))
            Write-Output ('来源目录：'+$(if($snapshot.SourceRoot){$snapshot.SourceRoot}else{'未记录；不含待迁移的组件路径'}))
            if ($snapshot.Manifest) {
                Write-Output ('快照/导出日期：'+$snapshot.Manifest.Created)
                if ($snapshot.Manifest.ConfigSavedAt) { Write-Output ('源配置保存日期：'+$snapshot.Manifest.ConfigSavedAt) }
            }
            Write-Output '覆盖所含组件的设置，缺少的组件保持现有设置；保留目标机认证信息。Icaros 仅在勾选后导入。'
            Write-Output '组件路径自动适配当前目录；不修改 Windows 默认应用、不运行包内程序。'
        }
        'Export' { Export-Package }
        'DeleteBackups' { Remove-Backups }
        default { throw '未知操作。' }
    }
} catch {
    $exitCode = 1
    Write-Output ('操作未完成：'+$_.Exception.Message)
} finally {
    if ($locked) { $lock.ReleaseMutex() }
    if ($lock) { $lock.Dispose() }
    foreach ($folder in $script:temporary) {
        $resolved = [IO.Path]::GetFullPath($folder)
        $prefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\PotPlayerManager-'
        if (!$resolved.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)) { throw '临时文件清理路径不正确。' }
        if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
    }
}
exit $exitCode
