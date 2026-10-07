# PotPlayer Portable 管理工具

面向 Windows x64 的 PotPlayer 便携包管理工具，包含原生配置管理窗口、迁移脚本和右方向键长按倍速启动器。

本仓库存放工具源码和说明。完整运行包单独通过 GitHub Releases 提供；仅下载仓库源码无法直接播放视频。

## 下载与使用

1. 从 [Releases](https://github.com/NeoLi-Coder/PotPlayer-Portable/releases) 下载 `PotPlayer-Portable-2026.10.07.zip`，解压到准备长期保留的目录。
2. 双击 `PotPlayer管理.exe` 或 `启动.cmd`，点击“初始化本目录”。
3. 点击“注册组件”，按系统提示授予管理员权限。
4. 按需创建桌面快捷方式和登记视频关联，然后从管理窗口启动播放器。

换电脑、换用户或移动目录后，重新初始化并注册组件；移动后也需要更新快捷方式和视频关联。

## 功能

- 保存、恢复、搜索和删除配置备份。
- 导出完整便携包或仅配置包，支持 ZIP 和文件夹；导入时检查配置清单及文件完整性。
- 完整包导出排除旧备份、配置快照、播放记录和初始化位置标记。
- 保存和导出前检查相关程序；需要关闭时提示用户确认，取消则停止操作。
- 短按右方向键保留原快进；按住约 0.3 秒进入 3 倍速，松开恢复 1 倍速。

长按功能需要通过 `PotPlayerPortable.exe`、管理窗口、所创建的快捷方式或登记的视频关联启动。直接运行 `x64/PotPlayerMini64.exe` 不会启用。请保持播放器 0.1 倍的倍速调节步长。

## 目录与编译

| 文件 | 用途 |
| --- | --- |
| `Tools/PortableManager.cs` | WinForms 管理窗口 |
| `Tools/Portable.Core.ps1` | 配置保存、恢复、导入、导出与组件登记 |
| `Tools/PotPlayerPortable.cs` | 播放器启动器和右方向键长按逻辑 |
| `build.ps1` | 使用 Windows 自带 .NET Framework 编译器编译两个工具 |
| `使用说明.txt` | 完整便携包操作说明 |

在 Windows PowerShell 中执行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

生成的两个 EXE 位于仓库根目录。将它们及 `Tools` 目录放入完整便携包对应位置使用。编译不需要 Node、Python 或第三方 NuGet 包，需要 Windows 自带 .NET Framework 4.x 编译器。

管理窗口按 UTF-8 显式读取核心脚本。手动调用核心脚本时，Windows PowerShell 5.1 应采用相同方式，避免无 BOM 的中文脚本被按系统代码页解析：

```powershell
$root = (Get-Location).Path
$core = Join-Path $root 'Tools\Portable.Core.ps1'
& ([scriptblock]::Create([IO.File]::ReadAllText($core, [Text.Encoding]::UTF8))) -RootPath $root -Action Check
```

## 配置边界

这里的“便携”表示无需安装程序即可迁移目录；播放器设置仍使用当前用户注册表，组件登记会写入系统。相同用户下不同 PotPlayer 目录会共享设置。

随包 madVR 画质配置针对 RX 6700、2560×1440 / 120Hz 显示输出，换设备后需要检查显示器、HDR、音频和解码设置。它不是所有设备通用的画质预设。

发布包采用最后保存的 Data 配置，不读取当前未保存的播放器设置。具体操作见 [使用说明](使用说明.txt)。

## 第三方软件与许可

本项目不是 PotPlayer、madVR 或其他组件的官方项目。播放器、滤镜、皮肤、图标等第三方内容的权利归各自作者；源码仓库与完整运行包分开发布。

保留包内已有的版权和许可文件，包括 `x64/License.txt`。当前未为本项目另行指定开源许可证，也不以本仓库声明替代第三方许可。发布完整包前需核实所含组件版本的再分发条件。

发布操作见 [发布说明](发布说明.md)。