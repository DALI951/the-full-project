param(
    [Parameter(Mandatory=$false)]
    [string]$ProjectPath = $PWD
)

$projectFullPath = Resolve-Path $ProjectPath
$manifestPath = Join-Path $projectFullPath "Packages\manifest.json"

if (-not (Test-Path $manifestPath)) {
    Write-Error "No manifest.json found at $manifestPath. Is this a Unity project?"
    exit 1
}

$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json

if ($manifest.dependencies.PSObject.Properties.Name -contains "com.lilac.unity-parrelsync") {
    Write-Host "ParrelSync is already installed." -ForegroundColor Green
    exit 0
}

$openupmRegistry = @{
    "name" = "package.openupm.com"
    "url" = "https://package.openupm.com"
    "scopes" = @("com.lilac")
}

$registries = @($manifest.scopedRegistries)
$exists = $false
foreach ($reg in $registries) {
    if ($reg.url -eq "https://package.openupm.com") {
        $exists = $true
        break
    }
}

if (-not $exists) {
    Write-Host "Adding OpenUPM scoped registry..." -ForegroundColor Cyan
    if (-not $registries) {
        $registries = @()
    }
    $registries += $openupmRegistry
    $manifest | Add-Member -MemberType NoteProperty -Name "scopedRegistries" -Value @($registries) -Force
}

if ($manifest.dependencies.PSObject.Properties.Name -contains "com.lilac.unity-parrelsync") {
    Write-Host "ParrelSync is already installed." -ForegroundColor Green
} else {
    $manifest.dependencies | Add-Member -MemberType NoteProperty -Name "com.lilac.unity-parrelsync" -Value "1.9.2" -Force
}

$manifest | ConvertTo-Json -Depth 10 | Set-Content $manifestPath -Encoding UTF8

Write-Host ""
Write-Host "ParrelSync v1.9.2 added to manifest.json." -ForegroundColor Green
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Open the project in Unity Editor" -ForegroundColor White
Write-Host "  2. Window > ParrelSync > Clones Manager" -ForegroundColor White
Write-Host "  3. Create a new clone for multiplayer testing" -ForegroundColor White
Write-Host ""
Write-Host "Pro tip: Create one clone (Client), keep original as Host." -ForegroundColor Cyan
Write-Host "Run both simultaneously to test multiplayer." -ForegroundColor Cyan
