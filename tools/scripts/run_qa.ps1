# Run QA Integration Tests
$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\\..")
$TestDir = Join-Path $RepoRoot "tests\\integration"
Push-Location $TestDir
npm install --silent
npm test
Pop-Location
