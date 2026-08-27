<#
.SYNOPSIS
    Starts FeedbackService and opens the KAN-1 widget demo page in the browser.

.EXAMPLE
    .\run-demo.ps1
    .\run-demo.ps1 -Port 5300
#>
param(
    [int]$Port = 5299
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$env:ASPNETCORE_URLS = "http://localhost:$Port"
$demoUrl = "http://localhost:$Port/demo.html"

# Poll in the background and open the browser as soon as the service responds, so
# startup (dotnet build + host init) doesn't have to finish before this returns.
Start-Job -ScriptBlock {
    param($url)
    for ($i = 0; $i -lt 30; $i++) {
        try {
            $response = Invoke-WebRequest -UseBasicParsing -Uri $url -TimeoutSec 2
            if ($response.StatusCode -eq 200) {
                Start-Process $url
                return
            }
        } catch {}
        Start-Sleep -Seconds 1
    }
} -ArgumentList $demoUrl | Out-Null

Write-Host "Starting FeedbackService on http://localhost:$Port ..."
Write-Host "Demo page: $demoUrl (opens automatically once the service is up)"
Write-Host "Press Ctrl+C to stop."
Write-Host ""

dotnet run --project (Join-Path $root "FeedbackService")
