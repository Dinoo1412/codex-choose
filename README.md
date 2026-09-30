# Codex 模型中心

一个面向普通用户的 Windows 桌面应用，用来管理 Codex 的模型、服务商、API Key 和切换配置。

## 直接使用

双击 `CodexModelCenter.exe`，或双击 `启动 Codex 模型中心.cmd`。

应用不需要 PowerShell、Node.js 或 Python。Windows 10/11 通常已经具备运行所需的 .NET Framework。

## 功能

- 官方 ChatGPT 登录、自定义 Responses API 和本地服务
- 模型配置分类：我的模型、模板、自定义模型
- 搜索模型和服务商
- API Key 保存到当前 Windows 用户环境变量
- 设为当前模型时自动保留其他 Provider，避免旧对话出现 `provider not found`
- 写入前备份 `config.toml`，支持恢复最近备份
- `/models` 连接检查
- 配置数据使用原子写入，并自动生成 `.bak` 备份

## 文件

```text
CodexModelCenter.exe          # 可直接分发的应用
ModelCenter.cs                # 应用源码
启动 Codex 模型中心.cmd       # 快速启动入口
```

应用运行后会将用户配置保存到：

```text
%LOCALAPPDATA%\CodexModelCenter\profiles.json
```

Codex 配置仍写入：

```text
%USERPROFILE%\.codex\config.toml
```

API Key 不会写入项目文件。每个用户需要在自己的电脑上单独设置密钥。

## 编译

本机安装 .NET Framework 4.x 后执行：

```powershell
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /optimize+ /codepage:65001 /out:CodexModelCenter.exe /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll .\ModelCenter.cs
```

验证：

```powershell
.\CodexModelCenter.exe --self-test
```

成功输出：

```text
ModelCenter self-test: OK
```
