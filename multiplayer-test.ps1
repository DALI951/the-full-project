param(
    [Parameter(Mandatory=$false)]
    [ValidateSet("host", "client", "both", "all")]
    [string]$Mode = "both",

    [Parameter(Mandatory=$false)]
    [string]$ProjectPath = $PWD,

    [Parameter(Mandatory=$false)]
    [string]$UnityVersion = "2022.3.62f2",

    [Parameter(Mandatory=$false)]
    [int]$ClientCount = 1,

    [Parameter(Mandatory=$false)]
    [switch]$Headless
)

$unityExe = "C:\Program Files\Unity\Hub\Editor\$UnityVersion\Editor\Unity.exe"

if (-not (Test-Path $unityExe)) {
    Write-Error "Unity Editor not found at: $unityExe"
    exit 1
}

$projectFullPath = Resolve-Path $ProjectPath

function Start-UnityInstance {
    param(
        [string]$Title,
        [string]$Args
    )
    $logFile = Join-Path $projectFullPath "Logs\${Title}.log"
    $unityArgs = @(
        "-projectPath", "`"$projectFullPath`"",
        "-logFile", "`"$logFile`""
    )
    if ($Args) {
        $unityArgs += $Args
    }
    Write-Host "Starting $Title..." -ForegroundColor Cyan
    $proc = Start-Process -FilePath $unityExe -ArgumentList $unityArgs -PassThru -NoNewWindow:$false
    Write-Host "  PID: $($proc.Id) | Log: $logFile" -ForegroundColor Gray
}

switch ($Mode) {
    "host" {
        Start-UnityInstance -Title "Server" -Args ""
    }
    "client" {
        Start-UnityInstance -Title "Client-1" -Args ""
    }
    "both" {
        Start-UnityInstance -Title "Host" -Args ""
        Start-UnityInstance -Title "Client" -Args ""
    }
    "all" {
        Start-UnityInstance -Title "Host" -Args ""
        for ($i = 1; $i -le $ClientCount; $i++) {
            Start-UnityInstance -Title "Client-$i" -Args ""
        }
    }
}

Write-Host ""
Write-Host "Multiplayer test instances launched." -ForegroundColor Green
Write-Host "Use 'Get-Process Unity' to monitor." -ForegroundColor Yellow
