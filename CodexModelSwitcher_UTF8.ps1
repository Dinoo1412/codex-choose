param(
    [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

[System.Windows.Forms.Application]::EnableVisualStyles()

$script:AppDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$script:ProfilesPath = Join-Path $script:AppDirectory "profiles.json"
$script:CodexDirectory = Join-Path $env:USERPROFILE ".codex"
$script:CodexConfigPath = Join-Path $script:CodexDirectory "config.toml"
$script:BackupDirectory = Join-Path $script:CodexDirectory "codex-model-switcher-backups"
$script:ManagedBegin = "# BEGIN CODEX MODEL SWITCHER MANAGED PROVIDERS"
$script:ManagedEnd = "# END CODEX MODEL SWITCHER MANAGED PROVIDERS"
$script:Profiles = @()

function New-ProfileObject {
    param(
        [string]$Name = "新配置",
        [string]$ProviderId = "custom",
        [string]$Model = "",
        [string]$BaseUrl = "",
        [ValidateSet("builtin", "env", "openai", "command", "none")]
        [string]$AuthMode = "env",
        [string]$EnvKey = "CUSTOM_API_KEY",
        [string]$AuthCommand = "",
        [string[]]$AuthArgs = @(),
        [ValidateSet("disabled", "cached", "indexed", "live")]
        [string]$WebSearch = "disabled"
    )

    [pscustomobject]@{
        id = [guid]::NewGuid().ToString("N")
        name = $Name
        providerId = $ProviderId
        model = $Model
        baseUrl = $BaseUrl
        authMode = $AuthMode
        envKey = $EnvKey
        authCommand = $AuthCommand
        authArgs = @($AuthArgs)
        webSearch = $WebSearch
    }
}

function Initialize-Profiles {
    if (Test-Path -LiteralPath $script:ProfilesPath) {
        try {
            $loaded = [System.IO.File]::ReadAllText($script:ProfilesPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
            $script:Profiles = @($loaded)
            if ($script:Profiles.Count -gt 0) {
                return
            }
        } catch {
            [System.Windows.Forms.MessageBox]::Show(
                "profiles.json 无法读取，将创建默认配置。`r`n`r`n$($_.Exception.Message)",
                "配置读取失败",
                "OK",
                "Warning"
            ) | Out-Null
        }
    }

    $script:Profiles = @(
        (New-ProfileObject -Name "官方 ChatGPT / OpenAI" -ProviderId "openai" -Model "gpt-5.6-terra" -AuthMode "builtin" -WebSearch "live"),
        (New-ProfileObject -Name "OpenRouter 模板" -ProviderId "openrouter" -Model "请填写准确模型ID" -BaseUrl "https://openrouter.ai/api/v1" -AuthMode "env" -EnvKey "OPENROUTER_API_KEY"),
        (New-ProfileObject -Name "自定义 Responses API" -ProviderId "custom_responses" -Model "请填写模型ID" -BaseUrl "https://example.com/v1" -AuthMode "env" -EnvKey "CUSTOM_RESPONSES_API_KEY"),
        (New-ProfileObject -Name "OpenAI 登录代理" -ProviderId "openai_proxy" -Model "gpt-5.6-terra" -BaseUrl "https://proxy.example.com/v1" -AuthMode "openai"),
        (New-ProfileObject -Name "本地无认证 Provider" -ProviderId "local_responses" -Model "local-model" -BaseUrl "http://127.0.0.1:11434/v1" -AuthMode "none")
    )
    Save-Profiles
}

function Save-Profiles {
    $json = @($script:Profiles) | ConvertTo-Json -Depth 10
    [System.IO.File]::WriteAllText(
        $script:ProfilesPath,
        $json,
        [System.Text.UTF8Encoding]::new($true)
    )
}

function Escape-TomlString {
    param([AllowEmptyString()][string]$Value)
    if ($null -eq $Value) { return "" }
    return $Value.Replace("\", "\\").Replace('"', '\"').Replace("`r", "\r").Replace("`n", "\n")
}

function Quote-TomlString {
    param([AllowEmptyString()][string]$Value)
    return '"' + (Escape-TomlString $Value) + '"'
}

function Set-TopLevelTomlString {
    param(
        [string]$Content,
        [string]$Key,
        [string]$Value
    )

    $newline = if ($Content.Contains("`r`n")) { "`r`n" } else { "`n" }
    $lines = @($Content -split "`r?`n", 0)
    $firstTable = $lines.Count

    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^\s*\[') {
            $firstTable = $i
            break
        }
    }

    $replacement = "$Key = $(Quote-TomlString $Value)"
    $updated = $false
    for ($i = 0; $i -lt $firstTable; $i++) {
        if ($lines[$i] -match ('^\s*' + [regex]::Escape($Key) + '\s*=')) {
            $lines[$i] = $replacement
            $updated = $true
            break
        }
    }

    if (-not $updated) {
        $before = @($lines[0..([Math]::Max(0, $firstTable - 1))])
        $after = if ($firstTable -lt $lines.Count) { @($lines[$firstTable..($lines.Count - 1)]) } else { @() }
        if ($firstTable -eq 0) { $before = @() }
        $lines = @($replacement) + $before + $after
    }

    return ($lines -join $newline).TrimEnd() + $newline
}

function Remove-ManagedProviderBlock {
    param([string]$Content)
    $pattern = '(?ms)^' + [regex]::Escape($script:ManagedBegin) + '.*?^' + [regex]::Escape($script:ManagedEnd) + '\s*'
    return [regex]::Replace($Content, $pattern, "").TrimEnd()
}

function Validate-Profile {
    param($Profile)

    if ([string]::IsNullOrWhiteSpace($Profile.name)) { throw "配置名称不能为空。" }
    if ([string]::IsNullOrWhiteSpace($Profile.model) -or $Profile.model -match '^请填写') { throw "请填写准确的模型 ID。" }
    if ($Profile.providerId -notmatch '^[A-Za-z0-9_-]+$') { throw "Provider ID 只能包含字母、数字、下划线和短横线。" }

    if ($Profile.authMode -eq "builtin") {
        if ($Profile.providerId -ne "openai") { throw "内置登录模式的 Provider ID 必须是 openai。" }
        return
    }

    if ($Profile.providerId -in @("openai", "ollama", "lmstudio")) {
        throw "自定义配置不能使用 Codex 保留的 Provider ID：$($Profile.providerId)。"
    }

    if ([string]::IsNullOrWhiteSpace($Profile.baseUrl) -or $Profile.baseUrl -match 'example\.com') {
        throw "请填写真实的 Responses API Base URL。"
    }
    if ($Profile.baseUrl -notmatch '^https?://') { throw "Base URL 必须以 http:// 或 https:// 开头。" }
    if ($Profile.authMode -eq "env" -and [string]::IsNullOrWhiteSpace($Profile.envKey)) { throw "环境变量认证需要填写环境变量名称。" }
    if ($Profile.authMode -eq "command" -and [string]::IsNullOrWhiteSpace($Profile.authCommand)) { throw "命令认证需要填写取 Token 命令。" }
}

function Get-ProviderTomlBlock {
    param($Profile)

    if ($Profile.authMode -eq "builtin") { return "" }

    $provider = Escape-TomlString $Profile.providerId
    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("[model_providers.$provider]")
    $lines.Add("name = $(Quote-TomlString $Profile.name)")
    $lines.Add("base_url = $(Quote-TomlString ($Profile.baseUrl.TrimEnd('/')))")
    $lines.Add('wire_api = "responses"')

    switch ($Profile.authMode) {
        "env" {
            $lines.Add("env_key = $(Quote-TomlString $Profile.envKey)")
        }
        "openai" {
            $lines.Add('requires_openai_auth = true')
        }
        "command" {
            $lines.Add("")
            $lines.Add("[model_providers.$provider.auth]")
            $lines.Add("command = $(Quote-TomlString $Profile.authCommand)")
            $args = @($Profile.authArgs | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { Quote-TomlString $_ })
            if ($args.Count -gt 0) {
                $lines.Add("args = [$($args -join ', ')]")
            }
            $lines.Add("timeout_ms = 5000")
            $lines.Add("refresh_interval_ms = 300000")
        }
    }

    return ($lines -join [Environment]::NewLine)
}


function Test-TomlBasicSafety {
    param([string]$Content)

    # 轻量级保护：避免明显的重复顶层键/托管标记不完整。
    $topKeys = @("model", "model_provider", "web_search")
    foreach ($key in $topKeys) {
        $count = ([regex]::Matches($Content, "(?m)^\s*" + [regex]::Escape($key) + "\s*=")).Count
        if ($count -gt 1) {
            throw "生成的 config.toml 中存在重复顶层键：$key。已取消激活。"
        }
    }

    $beginCount = ([regex]::Matches($Content, "(?m)^" + [regex]::Escape($script:ManagedBegin) + "$")).Count
    $endCount = ([regex]::Matches($Content, "(?m)^" + [regex]::Escape($script:ManagedEnd) + "$")).Count
    if ($beginCount -ne $endCount -or $beginCount -gt 1) {
        throw "生成的 Provider 托管区块标记异常。已取消激活。"
    }
}

function Repair-CodexDesktopRegistration {
    $pkg = Get-AppxPackage -Name "OpenAI.Codex" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $pkg) {
        throw "未检测到 OpenAI Codex Desktop 的 AppX/MSIX 安装包。"
    }

    $manifest = Join-Path $pkg.InstallLocation "AppxManifest.xml"
    if (-not (Test-Path -LiteralPath $manifest)) {
        throw "找不到 Codex AppxManifest.xml：$manifest"
    }

    Add-AppxPackage -DisableDevelopmentMode -Register $manifest -ErrorAction Stop
}

function Backup-CodexConfig {
    param([string]$Prefix = "config")

    if (-not (Test-Path -LiteralPath $script:BackupDirectory)) {
        New-Item -ItemType Directory -Path $script:BackupDirectory -Force | Out-Null
    }

    if (Test-Path -LiteralPath $script:CodexConfigPath) {
        $stamp = Get-Date -Format "yyyyMMdd-HHmmss-fff"
        $backup = Join-Path $script:BackupDirectory "$Prefix.$stamp.toml"
        Copy-Item -LiteralPath $script:CodexConfigPath -Destination $backup -Force
        return $backup
    }

    return $null
}

function Write-CodexConfiguration {
    param($ActiveProfile)

    Validate-Profile $ActiveProfile

    $duplicates = @($script:Profiles | Group-Object providerId | Where-Object { $_.Count -gt 1 -and $_.Name -ne "openai" })
    if ($duplicates.Count -gt 0) {
        $names = ($duplicates | ForEach-Object { $_.Name }) -join ", "
        throw "存在重复 Provider ID：$names。请为每个自定义配置使用唯一的 Provider ID。"
    }

    if (-not (Test-Path -LiteralPath $script:CodexDirectory)) {
        New-Item -ItemType Directory -Path $script:CodexDirectory -Force | Out-Null
    }
    if (-not (Test-Path -LiteralPath $script:BackupDirectory)) {
        New-Item -ItemType Directory -Path $script:BackupDirectory -Force | Out-Null
    }

    $content = if (Test-Path -LiteralPath $script:CodexConfigPath) {
        Get-Content -LiteralPath $script:CodexConfigPath -Raw
    } else {
        ""
    }

    # 激活前始终备份。写入失败时会自动回滚。
    $backup = Backup-CodexConfig -Prefix "config.before-activate"

    try {
        $content = Remove-ManagedProviderBlock $content
        $content = Set-TopLevelTomlString -Content $content -Key "model" -Value $ActiveProfile.model
        $content = Set-TopLevelTomlString -Content $content -Key "model_provider" -Value $ActiveProfile.providerId
        $content = Set-TopLevelTomlString -Content $content -Key "web_search" -Value $ActiveProfile.webSearch

        # 只写当前激活的自定义 Provider。
        # 旧版会把 profiles.json 中所有 Provider 一次性写入 Codex 配置；
        # 某个未使用 Provider 的格式异常也可能污染整个 config.toml。
        if ($ActiveProfile.authMode -ne "builtin") {
            $block = Get-ProviderTomlBlock $ActiveProfile
            if (-not [string]::IsNullOrWhiteSpace($block)) {
                $managed = @(
                    $script:ManagedBegin
                    "# 此区块由 CodexModelSwitcher.ps1 生成；仅包含当前激活 Provider。"
                    $block
                    $script:ManagedEnd
                ) -join [Environment]::NewLine
                $content = $content.TrimEnd() + [Environment]::NewLine + [Environment]::NewLine + $managed + [Environment]::NewLine
            }
        }

        Test-TomlBasicSafety $content

        $tempPath = "$($script:CodexConfigPath).switcher.tmp"
        [System.IO.File]::WriteAllText($tempPath, $content, [System.Text.UTF8Encoding]::new($false))

        # 再读一遍，防止空写/异常写入。
        $verify = Get-Content -LiteralPath $tempPath -Raw
        if ([string]::IsNullOrWhiteSpace($verify)) {
            throw "临时 config.toml 写入结果为空。"
        }
        Test-TomlBasicSafety $verify

        Move-Item -LiteralPath $tempPath -Destination $script:CodexConfigPath -Force
    }
    catch {
        $tempPath = "$($script:CodexConfigPath).switcher.tmp"
        if (Test-Path -LiteralPath $tempPath) {
            Remove-Item -LiteralPath $tempPath -Force -ErrorAction SilentlyContinue
        }

        if ($null -ne $backup -and (Test-Path -LiteralPath $backup)) {
            Copy-Item -LiteralPath $backup -Destination $script:CodexConfigPath -Force
        }
        throw
    }
}

function Get-AuthModeLabel {
    param([string]$Mode)
    switch ($Mode) {
        "builtin" { "内置 OpenAI / ChatGPT 登录" }
        "env" { "环境变量 API Key" }
        "openai" { "OpenAI 登录认证（代理）" }
        "command" { "命令获取 Bearer Token" }
        "none" { "无认证 / 本地" }
        default { "环境变量 API Key" }
    }
}

function Get-AuthModeValue {
    param([string]$Label)
    switch ($Label) {
        "内置 OpenAI / ChatGPT 登录" { "builtin" }
        "环境变量 API Key" { "env" }
        "OpenAI 登录认证（代理）" { "openai" }
        "命令获取 Bearer Token" { "command" }
        "无认证 / 本地" { "none" }
        default { "env" }
    }
}

function Show-ErrorMessage {
    param([string]$Message)
    [System.Windows.Forms.MessageBox]::Show($Message, "操作失败", "OK", "Error") | Out-Null
}

function Show-InfoMessage {
    param([string]$Message)
    [System.Windows.Forms.MessageBox]::Show($Message, "Codex 模型切换器", "OK", "Information") | Out-Null
}

if ($SelfTest) {
    $sample = @'
# existing comment
personality = "pragmatic"

[features]
shell_tool = true

# BEGIN CODEX MODEL SWITCHER MANAGED PROVIDERS
[model_providers.old]
name = "Old"
# END CODEX MODEL SWITCHER MANAGED PROVIDERS
'@

    $sample = Remove-ManagedProviderBlock $sample
    $sample = Set-TopLevelTomlString -Content $sample -Key "model" -Value "vendor/model-a"
    $sample = Set-TopLevelTomlString -Content $sample -Key "model_provider" -Value "vendor_a"
    $sample = Set-TopLevelTomlString -Content $sample -Key "web_search" -Value "disabled"

    if ($sample -notmatch '(?m)^model = "vendor/model-a"$') { throw "SelfTest: model 写入失败。" }
    if ($sample -notmatch '(?m)^model_provider = "vendor_a"$') { throw "SelfTest: model_provider 写入失败。" }
    if ($sample -notmatch '(?m)^web_search = "disabled"$') { throw "SelfTest: web_search 写入失败。" }
    if ($sample -match 'model_providers\.old') { throw "SelfTest: 托管区块移除失败。" }
    if ($sample -notmatch '(?m)^\[features\]$') { throw "SelfTest: 原有配置未保留。" }

    $testProfile = New-ProfileObject -Name "Test" -ProviderId "test_provider" -Model "test-model" -BaseUrl "https://api.example.test/v1" -AuthMode "command" -AuthCommand "token-helper" -AuthArgs @("--scope", "codex")
    $block = Get-ProviderTomlBlock $testProfile
    if ($block -notmatch '\[model_providers\.test_provider\.auth\]') { throw "SelfTest: 命令认证区块生成失败。" }
    if ($block -notmatch 'args = \["--scope", "codex"\]') { throw "SelfTest: 命令参数生成失败。" }

    Write-Output "CodexModelSwitcher self-test: OK"
    exit 0
}

Initialize-Profiles

$form = New-Object System.Windows.Forms.Form
$form.Text = "Codex 模型与 Provider 切换器"
$form.Size = New-Object System.Drawing.Size(1040, 720)
$form.MinimumSize = New-Object System.Drawing.Size(920, 640)
$form.StartPosition = "CenterScreen"
$form.Font = New-Object System.Drawing.Font("Microsoft YaHei UI", 9)
$form.BackColor = [System.Drawing.Color]::FromArgb(246, 247, 249)

$title = New-Object System.Windows.Forms.Label
$title.Text = "Codex 模型与 Provider 切换器"
$title.Font = New-Object System.Drawing.Font("Microsoft YaHei UI", 18, [System.Drawing.FontStyle]::Bold)
$title.AutoSize = $true
$title.Location = New-Object System.Drawing.Point(24, 18)
$form.Controls.Add($title)

$subtitle = New-Object System.Windows.Forms.Label
$subtitle.Text = "官方登录、Responses 兼容 API、环境变量 Key、命令 Token 与本地 Provider"
$subtitle.ForeColor = [System.Drawing.Color]::FromArgb(86, 93, 105)
$subtitle.AutoSize = $true
$subtitle.Location = New-Object System.Drawing.Point(27, 56)
$form.Controls.Add($subtitle)

$leftPanel = New-Object System.Windows.Forms.Panel
$leftPanel.Location = New-Object System.Drawing.Point(24, 92)
$leftPanel.Size = New-Object System.Drawing.Size(290, 530)
$leftPanel.Anchor = "Top,Bottom,Left"
$leftPanel.BackColor = [System.Drawing.Color]::White
$leftPanel.BorderStyle = "FixedSingle"
$form.Controls.Add($leftPanel)

$profileLabel = New-Object System.Windows.Forms.Label
$profileLabel.Text = "配置列表"
$profileLabel.Font = New-Object System.Drawing.Font("Microsoft YaHei UI", 10, [System.Drawing.FontStyle]::Bold)
$profileLabel.AutoSize = $true
$profileLabel.Location = New-Object System.Drawing.Point(14, 14)
$leftPanel.Controls.Add($profileLabel)

$profileList = New-Object System.Windows.Forms.ListBox
$profileList.Location = New-Object System.Drawing.Point(14, 42)
$profileList.Size = New-Object System.Drawing.Size(260, 400)
$profileList.Anchor = "Top,Bottom,Left,Right"
$profileList.BorderStyle = "FixedSingle"
$profileList.DisplayMember = "name"
$leftPanel.Controls.Add($profileList)

$addButton = New-Object System.Windows.Forms.Button
$addButton.Text = "新增"
$addButton.Location = New-Object System.Drawing.Point(14, 458)
$addButton.Size = New-Object System.Drawing.Size(78, 34)
$addButton.Anchor = "Bottom,Left"
$leftPanel.Controls.Add($addButton)

$duplicateButton = New-Object System.Windows.Forms.Button
$duplicateButton.Text = "复制"
$duplicateButton.Location = New-Object System.Drawing.Point(100, 458)
$duplicateButton.Size = New-Object System.Drawing.Size(78, 34)
$duplicateButton.Anchor = "Bottom,Left"
$leftPanel.Controls.Add($duplicateButton)

$deleteButton = New-Object System.Windows.Forms.Button
$deleteButton.Text = "删除"
$deleteButton.Location = New-Object System.Drawing.Point(186, 458)
$deleteButton.Size = New-Object System.Drawing.Size(78, 34)
$deleteButton.Anchor = "Bottom,Left"
$leftPanel.Controls.Add($deleteButton)

$rightPanel = New-Object System.Windows.Forms.Panel
$rightPanel.Location = New-Object System.Drawing.Point(330, 92)
$rightPanel.Size = New-Object System.Drawing.Size(680, 530)
$rightPanel.Anchor = "Top,Bottom,Left,Right"
$rightPanel.BackColor = [System.Drawing.Color]::White
$rightPanel.BorderStyle = "FixedSingle"
$rightPanel.AutoScroll = $true
$form.Controls.Add($rightPanel)

function Add-FieldLabel {
    param([string]$Text, [int]$X, [int]$Y)
    $label = New-Object System.Windows.Forms.Label
    $label.Text = $Text
    $label.AutoSize = $true
    $label.Location = New-Object System.Drawing.Point($X, $Y)
    $rightPanel.Controls.Add($label)
}

Add-FieldLabel "配置名称" 20 20
$nameBox = New-Object System.Windows.Forms.TextBox
$nameBox.Location = New-Object System.Drawing.Point(20, 43)
$nameBox.Size = New-Object System.Drawing.Size(305, 26)
$rightPanel.Controls.Add($nameBox)

Add-FieldLabel "Provider ID" 345 20
$providerBox = New-Object System.Windows.Forms.TextBox
$providerBox.Location = New-Object System.Drawing.Point(345, 43)
$providerBox.Size = New-Object System.Drawing.Size(305, 26)
$rightPanel.Controls.Add($providerBox)

Add-FieldLabel "模型 ID" 20 82
$modelBox = New-Object System.Windows.Forms.TextBox
$modelBox.Location = New-Object System.Drawing.Point(20, 105)
$modelBox.Size = New-Object System.Drawing.Size(630, 26)
$rightPanel.Controls.Add($modelBox)

Add-FieldLabel "Responses API Base URL" 20 144
$baseUrlBox = New-Object System.Windows.Forms.TextBox
$baseUrlBox.Location = New-Object System.Drawing.Point(20, 167)
$baseUrlBox.Size = New-Object System.Drawing.Size(630, 26)
$rightPanel.Controls.Add($baseUrlBox)

Add-FieldLabel "认证模式" 20 206
$authModeBox = New-Object System.Windows.Forms.ComboBox
$authModeBox.Location = New-Object System.Drawing.Point(20, 229)
$authModeBox.Size = New-Object System.Drawing.Size(305, 28)
$authModeBox.DropDownStyle = "DropDownList"
@(
    "内置 OpenAI / ChatGPT 登录",
    "环境变量 API Key",
    "OpenAI 登录认证（代理）",
    "命令获取 Bearer Token",
    "无认证 / 本地"
) | ForEach-Object { [void]$authModeBox.Items.Add($_) }
$rightPanel.Controls.Add($authModeBox)

Add-FieldLabel "Web Search" 345 206
$webSearchBox = New-Object System.Windows.Forms.ComboBox
$webSearchBox.Location = New-Object System.Drawing.Point(345, 229)
$webSearchBox.Size = New-Object System.Drawing.Size(305, 28)
$webSearchBox.DropDownStyle = "DropDownList"
@("disabled", "cached", "indexed", "live") | ForEach-Object { [void]$webSearchBox.Items.Add($_) }
$rightPanel.Controls.Add($webSearchBox)

Add-FieldLabel "API Key 环境变量名" 20 270
$envKeyBox = New-Object System.Windows.Forms.TextBox
$envKeyBox.Location = New-Object System.Drawing.Point(20, 293)
$envKeyBox.Size = New-Object System.Drawing.Size(440, 26)
$rightPanel.Controls.Add($envKeyBox)

$setKeyButton = New-Object System.Windows.Forms.Button
$setKeyButton.Text = "设置/更新 API Key"
$setKeyButton.Location = New-Object System.Drawing.Point(470, 289)
$setKeyButton.Size = New-Object System.Drawing.Size(180, 34)
$rightPanel.Controls.Add($setKeyButton)

Add-FieldLabel "取 Token 命令（命令认证时使用）" 20 333
$commandBox = New-Object System.Windows.Forms.TextBox
$commandBox.Location = New-Object System.Drawing.Point(20, 356)
$commandBox.Size = New-Object System.Drawing.Size(305, 26)
$rightPanel.Controls.Add($commandBox)

Add-FieldLabel "命令参数（每行一个）" 345 333
$argsBox = New-Object System.Windows.Forms.TextBox
$argsBox.Location = New-Object System.Drawing.Point(345, 356)
$argsBox.Size = New-Object System.Drawing.Size(305, 72)
$argsBox.Multiline = $true
$argsBox.ScrollBars = "Vertical"
$rightPanel.Controls.Add($argsBox)

$saveButton = New-Object System.Windows.Forms.Button
$saveButton.Text = "保存配置"
$saveButton.Location = New-Object System.Drawing.Point(20, 452)
$saveButton.Size = New-Object System.Drawing.Size(130, 42)
$saveButton.BackColor = [System.Drawing.Color]::FromArgb(229, 234, 242)
$rightPanel.Controls.Add($saveButton)

$activateButton = New-Object System.Windows.Forms.Button
$activateButton.Text = "激活到 Codex"
$activateButton.Location = New-Object System.Drawing.Point(160, 452)
$activateButton.Size = New-Object System.Drawing.Size(150, 42)
$activateButton.BackColor = [System.Drawing.Color]::FromArgb(26, 115, 232)
$activateButton.ForeColor = [System.Drawing.Color]::White
$activateButton.FlatStyle = "Flat"
$rightPanel.Controls.Add($activateButton)

$testButton = New-Object System.Windows.Forms.Button
$testButton.Text = "测试 /models"
$testButton.Location = New-Object System.Drawing.Point(320, 452)
$testButton.Size = New-Object System.Drawing.Size(130, 42)
$rightPanel.Controls.Add($testButton)

$openConfigButton = New-Object System.Windows.Forms.Button
$openConfigButton.Text = "打开 config.toml"
$openConfigButton.Location = New-Object System.Drawing.Point(460, 452)
$openConfigButton.Size = New-Object System.Drawing.Size(150, 42)
$rightPanel.Controls.Add($openConfigButton)

$repairDesktopButton = New-Object System.Windows.Forms.Button
$repairDesktopButton.Text = "修复 Codex Desktop"
$repairDesktopButton.Location = New-Object System.Drawing.Point(460, 497)
$repairDesktopButton.Size = New-Object System.Drawing.Size(150, 34)
$rightPanel.Controls.Add($repairDesktopButton)


$restoreButton = New-Object System.Windows.Forms.Button
$restoreButton.Text = "恢复最近备份"
$restoreButton.Location = New-Object System.Drawing.Point(500, 16)
$restoreButton.Size = New-Object System.Drawing.Size(150, 30)
$restoreButton.Anchor = "Top,Right"
$leftPanel.Controls.Add($restoreButton)
$restoreButton.Visible = $false

$statusLabel = New-Object System.Windows.Forms.Label
$statusLabel.Text = "就绪。激活配置后请完全退出并重新打开 Codex。"
$statusLabel.ForeColor = [System.Drawing.Color]::FromArgb(70, 78, 90)
$statusLabel.AutoSize = $true
$statusLabel.Location = New-Object System.Drawing.Point(26, 640)
$statusLabel.Anchor = "Bottom,Left"
$form.Controls.Add($statusLabel)

$restoreBottomButton = New-Object System.Windows.Forms.Button
$restoreBottomButton.Text = "恢复最近备份"
$restoreBottomButton.Location = New-Object System.Drawing.Point(848, 632)
$restoreBottomButton.Size = New-Object System.Drawing.Size(160, 34)
$restoreBottomButton.Anchor = "Bottom,Right"
$form.Controls.Add($restoreBottomButton)

function Refresh-ProfileList {
    param([string]$SelectId = "")
    $profileList.Items.Clear()
    foreach ($profile in $script:Profiles) {
        [void]$profileList.Items.Add($profile)
    }
    if ($profileList.Items.Count -gt 0) {
        $index = 0
        if (-not [string]::IsNullOrWhiteSpace($SelectId)) {
            for ($i = 0; $i -lt $profileList.Items.Count; $i++) {
                if ($profileList.Items[$i].id -eq $SelectId) { $index = $i; break }
            }
        }
        $profileList.SelectedIndex = $index
    }
}

function Update-AuthControls {
    $mode = Get-AuthModeValue $authModeBox.SelectedItem
    $isBuiltin = $mode -eq "builtin"
    $baseUrlBox.Enabled = -not $isBuiltin
    $providerBox.Enabled = -not $isBuiltin
    $envKeyBox.Enabled = $mode -eq "env"
    $setKeyButton.Enabled = $mode -eq "env"
    $commandBox.Enabled = $mode -eq "command"
    $argsBox.Enabled = $mode -eq "command"
    if ($isBuiltin) { $providerBox.Text = "openai" }
}

function Load-SelectedProfile {
    $profile = $profileList.SelectedItem
    if ($null -eq $profile) { return }
    $nameBox.Text = $profile.name
    $providerBox.Text = $profile.providerId
    $modelBox.Text = $profile.model
    $baseUrlBox.Text = $profile.baseUrl
    $authModeBox.SelectedItem = Get-AuthModeLabel $profile.authMode
    $envKeyBox.Text = $profile.envKey
    $commandBox.Text = $profile.authCommand
    $argsBox.Lines = @($profile.authArgs)
    $webSearchBox.SelectedItem = $profile.webSearch
    Update-AuthControls
}

function Update-SelectedProfileFromForm {
    $profile = $profileList.SelectedItem
    if ($null -eq $profile) { throw "请先选择或新增配置。" }
    $profile.name = $nameBox.Text.Trim()
    $profile.providerId = $providerBox.Text.Trim()
    $profile.model = $modelBox.Text.Trim()
    $profile.baseUrl = $baseUrlBox.Text.Trim()
    $profile.authMode = Get-AuthModeValue $authModeBox.SelectedItem
    $profile.envKey = $envKeyBox.Text.Trim()
    $profile.authCommand = $commandBox.Text.Trim()
    $profile.authArgs = @($argsBox.Lines | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    $profile.webSearch = [string]$webSearchBox.SelectedItem
    Validate-Profile $profile
    return $profile
}

$profileList.Add_SelectedIndexChanged({ Load-SelectedProfile })
$authModeBox.Add_SelectedIndexChanged({ Update-AuthControls })

$addButton.Add_Click({
    $profile = New-ProfileObject -Name "新 Responses Provider" -ProviderId ("provider_" + (Get-Random -Minimum 1000 -Maximum 9999))
    $script:Profiles += $profile
    Save-Profiles
    Refresh-ProfileList -SelectId $profile.id
})

$duplicateButton.Add_Click({
    try {
        $source = $profileList.SelectedItem
        if ($null -eq $source) { throw "请先选择一个配置。" }
        $profile = New-ProfileObject `
            -Name ($source.name + " - 副本") `
            -ProviderId ($source.providerId + "_copy") `
            -Model $source.model `
            -BaseUrl $source.baseUrl `
            -AuthMode $source.authMode `
            -EnvKey $source.envKey `
            -AuthCommand $source.authCommand `
            -AuthArgs @($source.authArgs) `
            -WebSearch $source.webSearch
        $script:Profiles += $profile
        Save-Profiles
        Refresh-ProfileList -SelectId $profile.id
    } catch { Show-ErrorMessage $_.Exception.Message }
})

$deleteButton.Add_Click({
    $profile = $profileList.SelectedItem
    if ($null -eq $profile) { return }
    if ($profile.authMode -eq "builtin") {
        Show-ErrorMessage "为避免失去官方回退入口，内置 OpenAI 配置不能删除。"
        return
    }
    $answer = [System.Windows.Forms.MessageBox]::Show(
        "确定删除配置 [$($profile.name)] 吗？不会删除 Codex 登录或聊天记录。",
        "确认删除",
        "YesNo",
        "Question"
    )
    if ($answer -eq "Yes") {
        $script:Profiles = @($script:Profiles | Where-Object { $_.id -ne $profile.id })
        Save-Profiles
        Refresh-ProfileList
    }
})

$saveButton.Add_Click({
    try {
        $profile = Update-SelectedProfileFromForm
        Save-Profiles
        Refresh-ProfileList -SelectId $profile.id
        $statusLabel.Text = "已保存配置：$($profile.name)"
    } catch { Show-ErrorMessage $_.Exception.Message }
})

$activateButton.Add_Click({
    try {
        $profile = Update-SelectedProfileFromForm
        Save-Profiles
        Write-CodexConfiguration $profile
        $statusLabel.Text = "已激活 $($profile.name)。请完全退出并重新打开 Codex。"
        Show-InfoMessage "已安全激活：$($profile.name)`r`n模型：$($profile.model)`r`n`r`n已在写入前备份原配置，并且仅写入当前 Provider。`r`n请完全退出并重新打开 Codex。"
    } catch { Show-ErrorMessage $_.Exception.Message }
})

$setKeyButton.Add_Click({
    try {
        $envName = $envKeyBox.Text.Trim()
        if ($envName -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') { throw "请先填写有效的环境变量名称。" }

        $dialog = New-Object System.Windows.Forms.Form
        $dialog.Text = "设置 $envName"
        $dialog.Size = New-Object System.Drawing.Size(520, 190)
        $dialog.StartPosition = "CenterParent"
        $dialog.FormBorderStyle = "FixedDialog"
        $dialog.MaximizeBox = $false
        $dialog.MinimizeBox = $false

        $label = New-Object System.Windows.Forms.Label
        $label.Text = "密钥将保存到 Windows 用户环境变量，不会写入工具配置文件。"
        $label.AutoSize = $true
        $label.Location = New-Object System.Drawing.Point(18, 18)
        $dialog.Controls.Add($label)

        $secretBox = New-Object System.Windows.Forms.TextBox
        $secretBox.Location = New-Object System.Drawing.Point(20, 52)
        $secretBox.Size = New-Object System.Drawing.Size(465, 26)
        $secretBox.UseSystemPasswordChar = $true
        $dialog.Controls.Add($secretBox)

        $ok = New-Object System.Windows.Forms.Button
        $ok.Text = "保存"
        $ok.Location = New-Object System.Drawing.Point(310, 96)
        $ok.Size = New-Object System.Drawing.Size(85, 32)
        $ok.DialogResult = "OK"
        $dialog.Controls.Add($ok)

        $cancel = New-Object System.Windows.Forms.Button
        $cancel.Text = "取消"
        $cancel.Location = New-Object System.Drawing.Point(400, 96)
        $cancel.Size = New-Object System.Drawing.Size(85, 32)
        $cancel.DialogResult = "Cancel"
        $dialog.Controls.Add($cancel)

        $dialog.AcceptButton = $ok
        $dialog.CancelButton = $cancel
        if ($dialog.ShowDialog($form) -eq "OK") {
            if ([string]::IsNullOrWhiteSpace($secretBox.Text)) { throw "密钥不能为空。" }
            [Environment]::SetEnvironmentVariable($envName, $secretBox.Text, "User")
            [Environment]::SetEnvironmentVariable($envName, $secretBox.Text, "Process")
            $secretBox.Text = ""
            Show-InfoMessage "已设置 $envName。`r`n激活配置后请重新启动 Codex，使其读取新的用户环境变量。"
        }
        $dialog.Dispose()
    } catch { Show-ErrorMessage $_.Exception.Message }
})

$testButton.Add_Click({
    try {
        $profile = Update-SelectedProfileFromForm
        if ($profile.authMode -eq "builtin") { throw "内置 OpenAI 登录不通过此按钮测试；请直接在 Codex 中使用。" }
        if ($profile.authMode -eq "openai") { throw "OpenAI 登录代理需要 Codex 的登录上下文，请激活后在 Codex 中测试。" }
        if ($profile.authMode -eq "command") { throw "命令认证由 Codex 执行，请激活后在 Codex 中测试。" }

        $headers = @{}
        if ($profile.authMode -eq "env") {
            $token = [Environment]::GetEnvironmentVariable($profile.envKey, "Process")
            if ([string]::IsNullOrWhiteSpace($token)) {
                $token = [Environment]::GetEnvironmentVariable($profile.envKey, "User")
            }
            if ([string]::IsNullOrWhiteSpace($token)) { throw "未找到环境变量 $($profile.envKey)。请先设置 API Key。" }
            $headers.Authorization = "Bearer $token"
        }

        $uri = $profile.baseUrl.TrimEnd('/') + "/models"
        $response = Invoke-RestMethod -Method Get -Uri $uri -Headers $headers -TimeoutSec 20
        $count = if ($null -ne $response.data) { @($response.data).Count } else { 0 }
        Show-InfoMessage "连接成功：$uri`r`n返回模型数量：$count`r`n`r`n注意：/models 成功不代表工具调用一定兼容。"
    } catch { Show-ErrorMessage $_.Exception.Message }
})

$openConfigButton.Add_Click({
    try {
        if (-not (Test-Path -LiteralPath $script:CodexConfigPath)) {
            if (-not (Test-Path -LiteralPath $script:CodexDirectory)) {
                New-Item -ItemType Directory -Path $script:CodexDirectory -Force | Out-Null
            }
            [System.IO.File]::WriteAllText($script:CodexConfigPath, "", [System.Text.UTF8Encoding]::new($false))
        }
        Start-Process notepad.exe -ArgumentList ('"' + $script:CodexConfigPath + '"')
    } catch { Show-ErrorMessage $_.Exception.Message }
})


$repairDesktopButton.Add_Click({
    try {
        $answer = [System.Windows.Forms.MessageBox]::Show(
            "将重新注册当前已安装的 OpenAI Codex Desktop AppX/MSIX。`r`n不会删除 .codex、登录信息或聊天数据。是否继续？",
            "修复 Codex Desktop",
            "YesNo",
            "Question"
        )
        if ($answer -ne "Yes") { return }

        Repair-CodexDesktopRegistration
        $statusLabel.Text = "Codex Desktop 注册已修复。"
        Show-InfoMessage "Codex Desktop AppX/MSIX 已重新注册。`r`n现在可以重新打开 Codex。"
    } catch {
        Show-ErrorMessage ("Codex Desktop 修复失败：`r`n" + $_.Exception.Message)
    }
})

$restoreBottomButton.Add_Click({
    try {
        if (-not (Test-Path -LiteralPath $script:BackupDirectory)) { throw "还没有可恢复的备份。" }
        $latest = Get-ChildItem -LiteralPath $script:BackupDirectory -Filter "config.*.toml" |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 1
        if ($null -eq $latest) { throw "还没有可恢复的备份。" }
        $answer = [System.Windows.Forms.MessageBox]::Show(
            "将恢复：$($latest.Name)`r`n当前 config.toml 会先生成额外备份。是否继续？",
            "恢复配置",
            "YesNo",
            "Question"
        )
        if ($answer -ne "Yes") { return }
        if (Test-Path -LiteralPath $script:CodexConfigPath) {
            $stamp = Get-Date -Format "yyyyMMdd-HHmmss-fff"
            Copy-Item -LiteralPath $script:CodexConfigPath -Destination (Join-Path $script:BackupDirectory "config.before-restore.$stamp.toml") -Force
        }
        Copy-Item -LiteralPath $latest.FullName -Destination $script:CodexConfigPath -Force
        Show-InfoMessage "已恢复 $($latest.Name)。请重新启动 Codex。"
    } catch { Show-ErrorMessage $_.Exception.Message }
})

$form.Add_Shown({ Refresh-ProfileList })
[void]$form.ShowDialog()
