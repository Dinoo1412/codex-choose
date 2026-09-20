# Codex 模型与 Provider 切换器

一个面向 Windows 的 PowerShell 图形化配置工具，用于管理多组模型和服务提供方（Provider）配置，并将选中的配置写入当前用户的 Codex `config.toml`，减少手动编辑配置文件的操作。

适合需要在内置 OpenAI 登录、自定义 Responses API 服务和本地服务之间切换的用户。界面使用中文，基于 Windows Forms，无需安装 Node.js、Python 或额外的 PowerShell 模块。

## 主要功能

- **管理多组配置**：新增、复制、编辑、保存和删除配置，设置模型 ID、Provider ID、API 地址和 Web Search 选项。
- **激活模型配置**：更新 `model`、`model_provider`、`web_search`，自定义 Provider 托管区块仅写入当前激活项。
- **多种认证方式**：内置 OpenAI / ChatGPT 登录、环境变量 API Key、OpenAI 登录代理、命令获取 Token、无认证服务。
- **设置 API Key**：通过密码输入框保存到 Windows 用户环境变量，不将密钥写入 `profiles.json`。
- **连接检查**：对环境变量认证和无认证服务请求 `/models`，检查基本连通性。
- **备份与恢复**：激活前备份已有 Codex 配置；写入失败时尝试从备份回滚；支持恢复最近备份。
- **辅助维护**：打开 `config.toml`，以及重新注册已安装的 Codex Desktop AppX/MSIX 应用包。

## 运行环境

- Windows 桌面环境，建议使用 Windows 10 / 11。
- Windows PowerShell 5.1（启动脚本使用 `powershell.exe`）及其 Windows Forms 支持。
- 已安装需要使用的 Codex 客户端，并能读取 `%USERPROFILE%\.codex\config.toml`。
- 使用在线服务时，需要相应账号、可用模型权限和网络连接。

本工具固定使用当前用户目录下的 `.codex`，没有读取 `CODEX_HOME` 或选择其他配置目录的功能。macOS、Linux 和无图形界面的环境不适用。

## 快速开始

### 1. 获取项目

```powershell
git clone https://github.com/Dinoo1412/codex-utf-8.git
cd codex-utf-8
```

也可以在 GitHub 仓库页面选择 **Code → Download ZIP**，解压后使用。私有仓库需要先获得访问权限。

### 2. 启动工具

双击项目目录中的 **`启动 Codex 模型切换器_UTF8.cmd`**。

或在项目目录运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -File .\CodexModelSwitcher_UTF8.ps1
```

启动参数中的 `ExecutionPolicy Bypass` 只应用于本次 PowerShell 进程，不修改系统的永久执行策略。请先确认脚本来源可信；组织策略仍可能限制运行。

### 3. 配置并激活

1. 从左侧选择一项配置，或点击“新增”。
2. 填写服务提供方支持的准确模型 ID；自定义服务还需填写 Provider ID 和 Base URL。
3. 选择认证模式；使用 API Key 时，填写环境变量名并点击“设置/更新 API Key”。
4. 点击“保存配置”。如适用，可点击“测试 /models”检查连接。
5. 点击“激活到 Codex”，随后**完全退出并重新打开 Codex**。

“保存配置”只更新工具自己的 `profiles.json`；“激活到 Codex”才会写入 Codex 配置文件，也会保存当前表单。

仓库内的 `profiles.json` 是一组示例和已有配置，不代表其中的模型或服务对所有用户可用。使用前请检查每项的模型 ID、Base URL 和认证模式，以实际字段为准，不要仅根据配置名称判断。已有文件存在时，程序优先读取它；文件缺失、为空或读取失败时会生成并保存脚本内的默认配置。

## 认证方式

| 界面选项 | 配置值 | 使用方式 |
| --- | --- | --- |
| 内置 OpenAI / ChatGPT 登录 | `builtin` | 使用 Codex 自身的登录状态，Provider ID 固定为 `openai`；本工具不负责登录。 |
| 环境变量 API Key | `env` | 通过指定环境变量读取密钥，生成 `env_key` 配置。 |
| OpenAI 登录认证（代理） | `openai` | 为自定义服务生成 `requires_openai_auth = true`；需要服务端及客户端支持对应登录认证。 |
| 命令获取 Bearer Token | `command` | 配置取 Token 命令及参数，由 Codex 执行；参数在界面中每行填写一个。 |
| 无认证 / 本地 | `none` | 不生成认证字段，用于无需凭证的服务。 |

命令认证生成的超时为 5 秒、刷新间隔为 5 分钟。内置登录、登录代理和命令认证均需在 Codex 中验证，不能使用工具的“测试 /models”按钮测试。

用户环境变量不是加密的凭证保险箱。API Key 输入框会隐藏内容，但密钥仍以环境变量形式保存在本机；不要将密钥填入配置名称、URL、命令参数或其他会随项目分享的字段。

## 配置字段

| 字段 | 说明 |
| --- | --- |
| `id` | 工具内部使用的配置标识，新增配置时自动生成。 |
| `name` | 配置列表中显示的名称。 |
| `providerId` | 仅允许字母、数字、下划线和短横线；自定义配置之间不能重复。 |
| `model` | 服务提供方实际支持的模型 ID，不能保留“请填写”占位文本。 |
| `baseUrl` | 服务 API 基础地址，必须以 `http://` 或 `https://` 开头；内置登录模式不使用此字段。 |
| `authMode` | 上表中的认证模式值。 |
| `envKey` | 保存 API Key 的环境变量名称，不是密钥本身。 |
| `authCommand` | 命令认证使用的可执行命令。 |
| `authArgs` | 命令参数数组。 |
| `webSearch` | 界面提供 `disabled`、`cached`、`indexed`、`live`；可用值及实际能力取决于客户端版本和服务支持。 |

自定义 Provider 不能使用工具保留的 `openai`、`ollama`、`lmstudio` 标识。即使连接本地服务，也请使用如 `local_responses` 的独立标识。

Base URL 应填写提供方要求的 API 根路径，例如带有 `/v1` 的地址，不要填写完整的 `/responses` 或 `/models` 请求地址。工具会在测试时自动追加 `/models`。

### 环境变量认证示例

下面展示 `profiles.json` 中一项配置的结构。域名和模型 ID 均需替换成你的实际服务信息，示例不能直接用于连接。

```json
{
  "id": "sample-responses-profile",
  "name": "我的 Responses 服务",
  "providerId": "my_responses",
  "model": "your-model-id",
  "baseUrl": "https://api.your-provider.invalid/v1",
  "authMode": "env",
  "envKey": "MY_RESPONSES_API_KEY",
  "authCommand": "",
  "authArgs": [],
  "webSearch": "disabled"
}
```

文件顶层应为配置数组 `[...]`。推荐通过界面修改；如需手动编辑，请先关闭工具并备份原文件。

## 文件与配置写入

```text
codex-utf-8/
├── CodexModelSwitcher_UTF8.ps1          # 图形界面、配置生成和自检逻辑
├── 启动 Codex 模型切换器_UTF8.cmd       # Windows 启动入口
├── profiles.json                       # 工具保存的配置列表
└── README.md
```

| 位置 | 用途 |
| --- | --- |
| 项目目录中的 `profiles.json` | 保存配置列表；不会自动从 Codex 配置中导入。 |
| `%USERPROFILE%\.codex\config.toml` | 激活时修改的 Codex 配置文件。 |
| `%USERPROFILE%\.codex\codex-model-switcher-backups\` | 保存带时间戳的配置备份。 |
| Windows 当前用户环境变量 | 保存通过界面设置的 API Key，并同时更新工具进程的环境变量。 |

激活时，程序更新三个顶层字段，并替换以下标记之间的 Provider 区块，保留其他配置文本：

```toml
# BEGIN CODEX MODEL SWITCHER MANAGED PROVIDERS
# 当前激活的自定义 Provider 配置
# END CODEX MODEL SWITCHER MANAGED PROVIDERS
```

切回内置登录时会移除该托管区块。程序执行的是文本级修改和基础校验，并非完整的 TOML 解析；如果在托管区块外手动定义了同名 Provider，需要自行检查冲突。

“恢复最近备份”按文件修改时间选择最新的 `config.*.toml`，恢复前还会备份当前配置。连续恢复可能选中上一次恢复前生成的备份；需要指定历史版本时，请打开备份目录按文件名核对后手动恢复。恢复范围仅为 `config.toml`，不包括配置列表和环境变量。

## 兼容性与常见问题

### `/models` 成功，但 Codex 无法使用模型

该按钮只发送 `GET <Base URL>/models`，不发起模型生成或工具调用。自定义配置固定生成 `wire_api = "responses"`，服务需要支持所用 Codex 客户端要求的 Responses API 行为；只支持 Chat Completions 的接口不一定可用。请核对模型 ID、接口兼容性和客户端错误信息。

### 修改或激活后没有生效

确认点击了“激活到 Codex”，而不只是“保存配置”。完全退出并重新启动 Codex，使其重新读取配置和环境变量。如果从旧终端启动 Codex，也请重新打开终端。再检查是否有其他配置来源覆盖了用户配置。

### 提示重复 Provider ID 或无效配置

复制配置后，为每个自定义配置设置唯一的 Provider ID。替换模型和地址占位符，并核对认证方式所需的环境变量名或命令。

### 激活后出现配置错误

使用“打开 config.toml”检查配置，或通过“恢复最近备份”恢复后重启 Codex。Web Search 选项、命令认证和 Provider 字段的支持情况受 Codex 版本影响，本工具不保证所有版本和第三方服务均兼容。

### “修复 Codex Desktop”能修复什么？

该操作查找已安装的 `OpenAI.Codex` AppX/MSIX 包并重新注册其清单。它不负责安装应用，也不修复模型服务、API Key 或网络问题；未检测到对应安装包时会报错。

## 开发与自检

本项目直接运行 PowerShell 脚本，无需构建。在项目目录执行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -File .\CodexModelSwitcher_UTF8.ps1 -SelfTest
```

成功时输出：

```text
CodexModelSwitcher self-test: OK
```

自检覆盖顶层字段更新、旧托管区块移除、其他配置保留和命令认证区块生成。它不打开主界面、不写入实际 Codex 配置，也不验证在线服务或完整 GUI 操作。

欢迎通过 [Issues](https://github.com/Dinoo1412/codex-utf-8/issues) 反馈问题。请提供 Windows / PowerShell / Codex 版本、复现步骤和去除密钥后的错误信息。修改代码时请保留现有脚本的 UTF-8 BOM 编码，以兼容 Windows PowerShell 对中文脚本的读取。
