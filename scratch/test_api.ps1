$loginUrl = "http://localhost:5110/Auth/Login"
$curriculumUrl = "http://localhost:5110/api/student/curriculum/33"

$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession

# 1. Login
$loginBody = @{
    username = "cuongnvcn0001"
    password = "123456"
}
Write-Host "Logging in..."
$response = Invoke-WebRequest -Uri $loginUrl -Method Post -Body $loginBody -WebSession $session -UseBasicParsing -ErrorAction Stop
Write-Host "Login successful. Cookies retrieved."

# 2. Get Curriculum
Write-Host "Calling curriculum API..."
try {
    $currResponse = Invoke-WebRequest -Uri $curriculumUrl -Method Get -WebSession $session -UseBasicParsing -ErrorAction Stop
    Write-Host "Curriculum API response:"
    $currResponse.Content
} catch {
    Write-Host "Error occurred:"
    Write-Host $_.Exception.Message
    if ($_.Exception.Response) {
        $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        $respBody = $reader.ReadToEnd()
        Write-Host "Response body:"
        Write-Host $respBody
    }
}
