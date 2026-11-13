#requires -Version 7.0
<#
.SYNOPSIS
Checks the Git upload file set and optionally creates a source-only ZIP.
.DESCRIPTION
Reads tracked files and non-ignored untracked files. Does not stage, commit or push.
The ZIP contains current working-tree bytes, not an existing Git commit.
#>
param([switch]$CreateArchive)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot

function Invoke-ProjectGit {
    param([string[]]$GitArguments)
    $output = & git -C $projectRoot @GitArguments
    if ($LASTEXITCODE -ne 0) { throw "Git command failed: git $($GitArguments -join ' ')" }
    return ($output -join [Environment]::NewLine)
}

$gitRoot = Invoke-ProjectGit @('rev-parse', '--show-toplevel')
if ([IO.Path]::GetFullPath($gitRoot) -ne [IO.Path]::GetFullPath($projectRoot)) {
    throw 'Initialize a repository in the project root: git init -b main'
}
$files = @((Invoke-ProjectGit @('ls-files', '-z', '--cached', '--others', '--exclude-standard')).
    Split([char]0, [StringSplitOptions]::RemoveEmptyEntries) | Sort-Object -Unique)
if (-not $files.Count) { throw 'No source files found.' }

$problems = [Collections.Generic.List[string]]::new()
$required = @(
    '.gitignore', '.gitattributes', 'README.md', 'LICENSE', 'THIRD-PARTY-NOTICES.md',
    'global.json', 'NuGet.Config', 'SC.ScriptForge.csproj', 'Build/BundledScripts.props',
    '.github/workflows/ci.yml', 'docs/GITHUB.md', 'Tools/Prepare-GitHub.ps1'
)
foreach ($requiredFile in $required) {
    if ($requiredFile -cnotin $files) { $problems.Add("Missing source file: $requiredFile") }
}

[xml]$bundle = Get-Content -LiteralPath (Join-Path $projectRoot 'Build/BundledScripts.props') -Raw
$libraryFiles = @($bundle.Project.ItemGroup.EmbeddedResource | ForEach-Object {
    $logicalName = [string]$_.LogicalName
    if (-not $logicalName.StartsWith('SC.ScriptForge.Library/', [StringComparison]::Ordinal)) {
        throw "Unexpected bundled resource: $logicalName"
    }
    'Data/Scripts/' + $logicalName.Substring('SC.ScriptForge.Library/'.Length)
})
foreach ($libraryFile in $libraryFiles) {
    if ($libraryFile -cnotin $files) { $problems.Add("Bundled resource excluded or missing: $libraryFile") }
}

# Detect forced additions of files that .gitignore would otherwise exclude.
$ignored = @((Invoke-ProjectGit @('ls-files', '-z', '--cached', '--ignored', '--exclude-standard')).
    Split([char]0, [StringSplitOptions]::RemoveEmptyEntries))
foreach ($ignoredFile in $ignored) { $problems.Add("Ignored file already in Git index: $ignoredFile") }

$blockedPath = '(?i)(^|/)(bin|obj|dist|artifacts|publish|packages|TestResults|\.tools|\.vs|\.idea|\.vscode|\.claude|\.codex|\.agents)/|^AI/Runtime/|\.(exe|dll|pdb|msi|msix|appx|gguf|safetensors|onnx|zip|7z|rar|tar|gz|pfx|p12|pem|key|snk|log|jsonl|dmp|db|sqlite3?|part|download|bak|tmp|user|suo)$|(^|/)\.env($|\.(?!example$))|(^|/)(ai-settings|ai-installation-folder|preferences|run-history|secrets|credentials)\.json$|\.local\.json$'
$secretPatterns = [ordered]@{
    'private key' = '-----BEGIN (?:RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----'
    'GitHub token' = '\b(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{30,})\b'
    'OpenAI-style API key' = '\bsk-(?:proj-|svcacct-)?[A-Za-z0-9_-]{32,}\b'
    'AWS access key ID' = '\b(?:AKIA|ASIA)[A-Z0-9]{16}\b'
    'literal credential' = '(?im)^\s*["'']?(?:api[_-]?key|access[_-]?token|client[_-]?secret|password)["'']?\s*[:=]\s*["''][^"''\r\n]{12,}["'']'
}
$binaryExtensions = @('.png', '.jpg', '.jpeg', '.ico', '.ttf', '.woff', '.woff2')
$totalBytes = 0L

foreach ($file in $files) {
    if ($file -match $blockedPath) { $problems.Add("Local or sensitive file in upload set: $file") }
    if ($file.StartsWith('Data/', [StringComparison]::OrdinalIgnoreCase) -and $file -cnotin $libraryFiles) {
        $problems.Add("User data outside the release allowlist: $file")
    }
    $fullPath = [IO.Path]::GetFullPath((Join-Path $projectRoot $file))
    if (-not $fullPath.StartsWith($projectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        $problems.Add("Path outside project: $file")
        continue
    }
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        $problems.Add("Indexed file is missing from working tree: $file")
        continue
    }
    $item = Get-Item -LiteralPath $fullPath -Force
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
        $problems.Add("Symbolic link or reparse point requires review: $file")
        continue
    }
    $totalBytes += $item.Length
    # Deliberately conservative source-repository policy, not the hosting limit.
    if ($item.Length -gt 50MB) {
        $problems.Add("Source file exceeds the project limit of 50 MiB: $file")
        continue
    }
    if ($item.Extension.ToLowerInvariant() -notin $binaryExtensions) {
        $content = [IO.File]::ReadAllText($fullPath)
        foreach ($pattern in $secretPatterns.GetEnumerator()) {
            if ($content -match $pattern.Value) { $problems.Add("Possible $($pattern.Key): $file") }
        }
    }
}

# Also inspect staged additions: the index can contain older bytes than the disk.
$stagedPatch = Invoke-ProjectGit @('diff', '--cached', '--no-ext-diff', '--no-textconv', '--no-color', '--unified=0')
$stagedAdditions = (($stagedPatch -split '\r?\n' | Where-Object {
    $_.StartsWith('+') -and -not $_.StartsWith('+++')
}) | ForEach-Object { $_.Substring(1) }) -join [Environment]::NewLine
foreach ($pattern in $secretPatterns.GetEnumerator()) {
    if ($stagedAdditions -match $pattern.Value) {
        $problems.Add("Possible $($pattern.Key) in staged additions; inspect git diff --cached locally.")
    }
}

if ($problems.Count) {
    foreach ($problem in $problems) { Write-Output "FAIL $problem" }
    throw "GitHub preparation failed: $($problems.Count) issue(s). No archive was created."
}

Write-Output ("PASS {0} files, {1:N2} MiB; {2} bundled scripts with metadata." -f
    $files.Count, ($totalBytes / 1MB), @($libraryFiles | Where-Object { $_.EndsWith('.ps1') }).Count)
Write-Output 'PASS No excluded files or known secret patterns found in the upload set.'

if ($CreateArchive) {
    $outputFolder = Join-Path $projectRoot 'artifacts/github'
    [IO.Directory]::CreateDirectory($outputFolder) | Out-Null
    # CreateNew intentionally refuses to overwrite an earlier source snapshot.
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
    $archivePath = Join-Path $outputFolder "SC-ScriptForge-source-$stamp.zip"
    $stream = [IO.File]::Open($archivePath, [IO.FileMode]::CreateNew)
    try {
        $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            foreach ($file in $files) {
                [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                    $zip, (Join-Path $projectRoot $file), $file,
                    [IO.Compression.CompressionLevel]::Optimal) | Out-Null
            }
        }
        finally { $zip.Dispose() }
    }
    finally { $stream.Dispose() }
    $manifestPath = [IO.Path]::ChangeExtension($archivePath, '.files.txt')
    [IO.File]::WriteAllLines($manifestPath, $files, [Text.UTF8Encoding]::new($false))
    $hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText($archivePath + '.sha256', "$hash  $([IO.Path]::GetFileName($archivePath))" + [Environment]::NewLine)
    Write-Output "Source ZIP: $archivePath"
    Write-Output "File list: $manifestPath"
    Write-Output "SHA256: $hash"
}
