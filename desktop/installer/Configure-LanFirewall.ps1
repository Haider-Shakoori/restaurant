param(
    [Parameter(Mandatory=$true)][ValidateRange(1,65535)][int]$Port
)
$ErrorActionPreference = "Stop"
$ruleName = "BusinessOS Restaurant LAN ($Port)"
$existing = Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue
if ($null -eq $existing) {
    New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Action Allow -Protocol TCP -LocalPort $Port -Profile Domain,Private | Out-Null
}
Write-Output "Firewall rule ready: $ruleName (Domain/Private only)"
