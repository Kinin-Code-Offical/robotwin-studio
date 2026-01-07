param (
    [int]$StartPort = 8090,
    [int]$EndPort = 8100
)

$ErrorActionPreference = "Stop"
$shutdownUrlTemplate = "http://127.0.0.1:{0}/api/shutdown"
$found = $false

for ($port = $StartPort; $port -le $EndPort; $port++) {
    $url = [string]::Format($shutdownUrlTemplate, $port)
    try {
        $response = Invoke-WebRequest -Method Post -Uri $url -TimeoutSec 2 -UseBasicParsing
        if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 300) {
            Write-Host "[Debug Console] Shutdown request sent to $url"
            $found = $true
            break
        }
    }
    catch {
        # Ignore and try next port.
    }
}

if (-not $found) {
    Write-Host "[Debug Console] No running debug console found (ports $StartPort-$EndPort)."
}

