# SnapIt 快截

SnapIt 是一款面向 Windows 的轻量截图与标注工具。启动后驻留系统托盘，并在桌面角落显示一个小型快捷入口；不需要常驻打开大型主窗口。

## 功能

- 截取主显示器区域，并可选择窗口或拖动调整选区。
- 使用矩形、椭圆、直线、箭头、画笔、荧光笔、马赛克和文字标注。
- 画直线时按住 `Shift` 可锁定为水平线。
- 撤销/重做、复制到剪贴板、保存截图，以及将截图置顶为贴图。
- 桌面提示图标可拖动；截图开始前会自动隐藏，避免被截入图片。
- 支持通过快捷键、桌面提示图标或系统托盘菜单启动截图。

> 当前版本截取主显示器；多显示器支持尚未实现。

## 方式一：下载 EXE 直接使用

从项目的 GitHub **Releases** 下载 `SnapIt.exe`，双击启动即可。发布的 Windows x64 单文件版本为 self-contained，不要求另外安装 .NET Runtime。

启动后，桌面角落会出现 SnapIt 小图标：

1. 单击图标开始截图；拖动右侧握柄可移动图标。
2. 默认全局截图快捷键为 `Alt+A`。若被占用，程序会尝试 `Ctrl+Alt+A`、`Ctrl+Shift+A`；当前使用的快捷键会通过启动提示和图标悬停提示显示。
3. 也可以右键桌面提示图标，或从系统托盘菜单选择“截图”。
4. 截图编辑工具栏中可标注、复制、保存或贴图。贴图可拖动、滚轮缩放，按 `Esc` 或双击可关闭。

若所有快捷键均无法注册，仍可单击桌面提示图标或从托盘菜单启动截图。SnapIt 不会自动接管 `PrintScreen`。

## 方式二：下载源码并构建

### 环境

- Windows x64
- .NET 8 SDK

可在 GitHub 仓库页面选择 **Code → Download ZIP** 下载源码压缩包并解压；也可以克隆仓库：

```powershell
git clone https://github.com/LELIU1/SnapIt.git
cd SnapIt
```

在仓库根目录执行：

```powershell
dotnet build .\SnapIt.sln --configuration Release
```

构建好的应用位于：

```text
src\SnapIt\bin\Release\net8.0-windows\SnapIt.exe
```

要运行验收测试：

```powershell
dotnet run --project .\tests\SnapIt.AcceptanceTests\SnapIt.AcceptanceTests.csproj --configuration Release
```

要自行发布无需安装 .NET Runtime 的 Windows x64 单文件程序：

```powershell
dotnet publish .\src\SnapIt\SnapIt.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  --output .\artifacts\publish\win-x64
```

发布后的程序位于 `artifacts\publish\win-x64\SnapIt.exe`。`artifacts`、`bin` 和 `obj` 都是本地生成内容，不需要提交到 Git。

## 项目结构

```text
SnapIt/
├── SnapIt.sln
├── src/
│   └── SnapIt/                       # WPF 应用源码与项目文件
│       ├── Capture/                  # 屏幕捕获、窗口选择
│       ├── Controls/                 # 自定义 WPF 控件
│       ├── Model/                    # 标注图元与编辑命令
│       ├── Services/                 # 快捷键、导出、马赛克
│       └── Windows/                  # 截图编辑、桌面图标、贴图、帮助窗口
└── tests/
    └── SnapIt.AcceptanceTests/       # 无第三方测试框架的验收程序
```

## 快捷键速查

| 快捷键 | 操作 |
| --- | --- |
| `Alt+A`（若冲突则使用提示中的备用键） | 启动截图 |
| `Ctrl+Z` | 撤销 |
| `Ctrl+Y` 或 `Ctrl+Shift+Z` | 重做 |
| `Ctrl+C` | 复制截图并退出编辑 |
| `Ctrl+S` | 保存截图 |
| `Esc` | 取消/退出；贴图窗口中关闭贴图 |
| 画直线时按住 `Shift` | 水平约束 |

## 许可

本项目以 [MIT License](LICENSE) 开源。使用、修改或分发时请保留版权声明和许可证文本。
