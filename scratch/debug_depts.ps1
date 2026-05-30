$loginUrl = "http://localhost:5110/Auth/Login"
$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$body = @{ username = "admin"; password = "123456" }
$res = Invoke-WebRequest -Uri $loginUrl -Method Post -Body $body -WebSession $session -UseBasicParsing
Write-Host "Login status: $($res.StatusCode)"

$deptsRes = Invoke-WebRequest -Uri "http://localhost:5110/api/it/departments" -Method Get -WebSession $session -UseBasicParsing
Write-Host "Depts raw content:"
$deptsRes.Content
