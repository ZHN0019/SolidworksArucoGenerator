# SolidworksArucoGenerator

[English](README_EN.md) | 简体中文

用于 SOLIDWORKS 的 ArUco 零件生成插件。插件使用 OpenCV
`DICT_4X4_50` 字典，可生成 ID `0-30` 的双实体 ArUco 零件、同名 PNG
图像和 STEP AP214 文件。

## 功能

- 可设置 ArUco 编号、码区边长、整体厚度和白色边缘宽度；
- 白色基板和白色图案组成一个连续实体；
- 黑色码元、背面编号及 `+X/+Y` 标记组成另一个连续实体；
- 输出 SLDPRT、PNG 和 STEP AP214；
- 按码区边长自动建立 `打印-<尺寸>` 文件夹；
- 同名文件存在时自动追加 `_2`、`_3`，不覆盖旧结果。

## 适配环境

| 项目 | 支持情况 |
|---|---|
| SOLIDWORKS 2025 x64 SP0.0 | 已完整验证 |
| SOLIDWORKS 2025 x64 其他 SP | 设计上兼容，未逐个验证 |
| SOLIDWORKS 2026 x64 | 可能兼容，未验证 |
| SOLIDWORKS 2024 及更早版本 | 不保证兼容 |
| 32 位 SOLIDWORKS | 不支持 |
| Windows | Windows 10 1809 x64 或更高 |
| .NET | .NET Framework 4.8 |

当前发布版本使用 SOLIDWORKS 2025 Revision 33 的 Interop DLL 构建。

## 下载与校验

安装包：

[`dist/SOLIDWORKS_ArUco_Generator_Setup_1.1.0_x64.exe`](dist/SOLIDWORKS_ArUco_Generator_Setup_1.1.0_x64.exe)

SHA-256：

```text
29acd63bab28240197796c790c9d0f527403eeef4e0a11ca6c11f6774d1a5e99
```

当前安装包没有商业代码签名证书，Windows SmartScreen 可能显示未知发布者提示。

## 安装

1. 保存工作并彻底关闭所有 SOLIDWORKS 窗口。
2. 运行 `SOLIDWORKS_ArUco_Generator_Setup_1.1.0_x64.exe`。
3. 接受 Windows 管理员权限提示并完成安装。
4. 启动 SOLIDWORKS。
5. 打开 `工具 > 插件`，确认“ArUco 零件生成器”已启用，并按需勾选启动项。
6. 从 `工具 > ArUco 生成器 > 生成 ArUco` 打开界面。

卸载前关闭 SOLIDWORKS，然后从 Windows“已安装的应用”中卸载。

## 使用

| 参数 | 说明 |
|---|---|
| ArUco 编号 | `0-30` |
| 码区边长 | 默认 `20 mm`，不包含额外白边 |
| 整体厚度 | 默认 `1 mm` |
| 白色边缘宽度 | 大于或等于 `0 mm`，默认 `0 mm` |
| 输出目录 | 保存不同尺寸结果的根目录 |

点击“生成模型”后，插件会创建并打开零件。例如码区边长为 40 mm：

```text
选择的输出目录/
└─ 打印-40/
   ├─ ArUco_DICT_4X4_50_ID07_S40_B0_T1.SLDPRT
   ├─ ArUco_DICT_4X4_50_ID07_S40_B0_T1.png
   └─ ArUco_DICT_4X4_50_ID07_S40_B0_T1.STEP
```

STEP 固定导出为 AP214，并启用外观数据。插件会在导出后恢复用户原来的
SOLIDWORKS STEP 设置。

## 从源码构建

前置条件：

- SOLIDWORKS 2025 x64；
- .NET SDK 8；
- .NET Framework 4.8 Developer Pack；
- 构建安装包时需要 Inno Setup 7 x64。

标准安装位置会自动检测。使用自定义 SOLIDWORKS 安装目录时，可设置：

```powershell
$env:SOLIDWORKS_INTEROP_PATH = "C:\path\to\solidworks\api\redist"
```

构建插件和验证程序：

```powershell
.\ArucoSolidWorksAddin\scripts\Build.ps1
```

构建安装包：

```powershell
.\ArucoSolidWorksAddin\scripts\BuildInstaller.ps1
```

## 隐私与安全

插件不包含网络访问、遥测、账号登录或数据上传功能。模型和图像只写入用户
选择的本地目录。公开内容在提交前已检查凭据、令牌、私钥、邮箱、Windows 用户名和
本机绝对路径；`bin/obj`、PDB、日志、验证样件及本机注册信息均未上传。

详见 [PRIVACY.md](PRIVACY.md) 和 [SECURITY.md](SECURITY.md)。

## 许可证

此仓库当前未声明开源许可证。未经版权所有者许可，不自动授予复制、修改或
分发源码的权利。
