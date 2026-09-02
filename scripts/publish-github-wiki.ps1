# Pushes wiki/*.md to https://github.com/PhoenixTheSage/Anomaly/wiki
# First time: open the Wiki tab and click "Create the first page" (Save an empty Home).
# That creates Anomaly.wiki.git. This script then overwrites it.

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$src = Join-Path $root "wiki"
if (-not (Test-Path $src)) { throw "Missing $src" }

$remote = "https://github.com/PhoenixTheSage/Anomaly.wiki.git"
$work = Join-Path $env:TEMP "anomaly-wiki-publish"
if (Test-Path $work) { Remove-Item -Recurse -Force $work }

$token = gh auth token
$authed = "https://x-access-token:${token}@github.com/PhoenixTheSage/Anomaly.wiki.git"
git clone $authed $work
if ($LASTEXITCODE -ne 0) {
    throw "Could not clone Anomaly.wiki.git. Enable Wikis and create the first page in the GitHub UI, then re-run."
}

Get-ChildItem $work -File | Where-Object { $_.Name -ne ".git" } | Remove-Item -Force
Copy-Item (Join-Path $src "*.md") $work
Set-Location $work
git add -A
$pending = git status --porcelain
if (-not $pending) {
    Write-Output "Wiki already up to date."
    exit 0
}
git commit -m "Publish shader developer wiki from repo wiki/."
git push origin HEAD
Write-Output "Published: https://github.com/PhoenixTheSage/Anomaly/wiki"
