$loginUrl = "http://localhost:5110/Auth/Login"
$coursesUrl = "http://localhost:5110/api/student/courses"
$dashboardUrl = "http://localhost:5110/api/student/dashboard"

$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession

# 1. Login
$loginBody = @{
    username = "cuongnvcn0001"
    password = "123456"
}
Write-Host "Logging in..."
$response = Invoke-WebRequest -Uri $loginUrl -Method Post -Body $loginBody -WebSession $session -UseBasicParsing -ErrorAction Stop
Write-Host "Login successful."

# 2. Get Dashboard
Write-Host "Calling dashboard API..."
try {
    $dashResponse = Invoke-WebRequest -Uri $dashboardUrl -Method Get -WebSession $session -UseBasicParsing -ErrorAction Stop
    Write-Host "Dashboard response:"
    $dashResponse.Content
} catch {
    Write-Host "Dashboard error:"
    Write-Host $_.Exception.Message
}

# 3. Get Courses
Write-Host "Calling courses API..."
try {
    $coursesResponse = Invoke-WebRequest -Uri $coursesUrl -Method Get -WebSession $session -UseBasicParsing -ErrorAction Stop
    Write-Host "Courses response:"
    $coursesResponse.Content
} catch {
    Write-Host "Courses error:"
    Write-Host $_.Exception.Message
}
