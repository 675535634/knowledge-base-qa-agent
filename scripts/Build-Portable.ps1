param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$ResetData
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$project = Join-Path $repoRoot "src\KnowledgeBaseQaAgent.Desktop\KnowledgeBaseQaAgent.Desktop.csproj"
$artifactsRoot = Join-Path $repoRoot "artifacts"
$publishRoot = Join-Path $artifactsRoot "portable"
$appRoot = Join-Path $publishRoot "KnowledgeBaseQaAgent"
$zipPath = Join-Path $artifactsRoot "KnowledgeBaseQaAgent-portable-$Runtime.zip"
$updateZipPath = Join-Path $artifactsRoot "KnowledgeBaseQaAgent-portable-update-$Runtime.zip"
$backupRoot = Join-Path $artifactsRoot "portable-data-backups"
$dataPath = Join-Path $appRoot "Data"
$toolsPath = Join-Path $appRoot "Tools"
$dataBackupPath = $null
$toolsBackupPath = $null

if ((Test-Path $dataPath) -and -not $ResetData) {
    New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
    $dataBackupPath = Join-Path $backupRoot ("Data-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
    Move-Item -LiteralPath $dataPath -Destination $dataBackupPath
    Write-Host "Preserved portable Data: $dataBackupPath"
}

if ((Test-Path $toolsPath) -and -not $ResetData) {
    New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
    $toolsBackupPath = Join-Path $backupRoot ("Tools-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
    Move-Item -LiteralPath $toolsPath -Destination $toolsBackupPath
    Write-Host "Preserved portable Tools: $toolsBackupPath"
}

if (Test-Path $appRoot) {
    Remove-Item -LiteralPath $appRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $appRoot -Force | Out-Null

dotnet publish $project `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $appRoot

if ($dataBackupPath) {
    $publishedDataPath = Join-Path $appRoot "Data"
    if (Test-Path $publishedDataPath) {
        Remove-Item -LiteralPath $publishedDataPath -Recurse -Force
    }

    Move-Item -LiteralPath $dataBackupPath -Destination $publishedDataPath
}

if ($toolsBackupPath) {
    $publishedToolsPath = Join-Path $appRoot "Tools"
    if (Test-Path $publishedToolsPath) {
        Remove-Item -LiteralPath $publishedToolsPath -Recurse -Force
    }

    Move-Item -LiteralPath $toolsBackupPath -Destination $publishedToolsPath
}

$obsoleteKokoroPaths = @(
    "Tools\Kokoro\kokoro-int8-multi-lang-v1_0",
    "Tools\Kokoro\kokoro-multi-lang-v1_0",
    "Tools\Kokoro\static-bin",
    "Tools\Kokoro\static-bin-v1.12.40",
    "Tools\Kokoro\include",
    "Tools\Kokoro\lib"
)
foreach ($relativePath in $obsoleteKokoroPaths) {
    $obsoletePath = Join-Path $appRoot $relativePath
    if (Test-Path $obsoletePath) {
        Remove-Item -LiteralPath $obsoletePath -Recurse -Force
    }
}

$obsoleteVitsPaths = @(
    "Tools\VITS\vits-zh-hf-theresa",
    "Tools\VITS\vits-zh-hf-eula"
)
foreach ($relativePath in $obsoleteVitsPaths) {
    $obsoletePath = Join-Path $appRoot $relativePath
    if (Test-Path $obsoletePath) {
        Remove-Item -LiteralPath $obsoletePath -Recurse -Force
    }
}

Get-ChildItem -LiteralPath $appRoot -Recurse -Filter *.pdb -Force |
    Remove-Item -Force

$logsPath = Join-Path $dataPath "logs"
if (Test-Path $logsPath) {
    Remove-Item -LiteralPath $logsPath -Recurse -Force
}

New-Item -ItemType Directory -Path $dataPath -Force | Out-Null
New-Item -ItemType File -Path (Join-Path $appRoot "portable.flag") -Force | Out-Null

@"
Knowledge Base QA Agent Portable

Run KnowledgeBaseQaAgent.Desktop.exe directly after extracting this folder.

Portable mode is enabled by portable.flag. Runtime data is stored in .\Data:
- settings.json
- secrets.json
- secret.key
- knowledge.db
- logs\

API keys saved in the admin console are encrypted in Data\secrets.json.
Keep Data\secret.key with the portable folder; copied packages need it to decrypt secrets.
Press Ctrl+1 or Ctrl+NumPad1 to open the administrator PIN dialog.

For batch deployment: configure this extracted folder once, exit the app,
then re-compress the whole folder including Data and copy it to other machines.

For safe updates: extract KnowledgeBaseQaAgent-portable-update-$Runtime.zip over
an existing portable folder. The update package intentionally excludes .\Data,
so settings, API keys, knowledge.db, logs, and custom knowledge-base data are kept.

Delete the folder to remove the portable app and its data.
"@ | Set-Content -Path (Join-Path $appRoot "PORTABLE-README.txt") -Encoding UTF8

if (Test-Path $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

Compress-Archive -Path (Join-Path $appRoot "*") -DestinationPath $zipPath -Force

if (Test-Path $updateZipPath) {
    Remove-Item -LiteralPath $updateZipPath -Force
}

$updateSource = Join-Path $artifactsRoot "portable-update"
$updateRoot = Join-Path $updateSource "KnowledgeBaseQaAgent"
if (Test-Path $updateSource) {
    Remove-Item -LiteralPath $updateSource -Recurse -Force
}

New-Item -ItemType Directory -Path $updateRoot -Force | Out-Null
Get-ChildItem -LiteralPath $appRoot -Force |
    Where-Object { $_.Name -ne "Data" } |
    ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $updateRoot -Recurse -Force
    }

Compress-Archive -Path (Join-Path $updateRoot "*") -DestinationPath $updateZipPath -Force

Write-Host "Portable folder: $appRoot"
Write-Host "Portable zip:    $zipPath"
Write-Host "Update zip:      $updateZipPath"
