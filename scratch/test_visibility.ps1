$loginUrl = "http://localhost:5110/Auth/Login"
$coursesUrl = "http://localhost:5110/api/student/courses"
$createCourseUrl = "http://localhost:5110/api/it/courses"

# Helper function to login and get courses
function Get-StudentCourses($username) {
    $session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $body = @{ username = $username; password = "123456" }
    $null = Invoke-WebRequest -Uri $loginUrl -Method Post -Body $body -WebSession $session -UseBasicParsing -ErrorAction Stop
    $response = Invoke-WebRequest -Uri $coursesUrl -Method Get -WebSession $session -UseBasicParsing -ErrorAction Stop
    return $response.Content | ConvertFrom-Json
}

# 1. Login as IT Admin
$sessionAdmin = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$bodyAdmin = @{ username = "admin"; password = "123456" }
$null = Invoke-WebRequest -Uri $loginUrl -Method Post -Body $bodyAdmin -WebSession $sessionAdmin -UseBasicParsing -ErrorAction Stop

# 2. Get department IDs
$itDeptsRes = Invoke-WebRequest -Uri "http://localhost:5110/api/it/departments" -Method Get -WebSession $sessionAdmin -UseBasicParsing -ErrorAction Stop
$itDepts = $itDeptsRes.Content | ConvertFrom-Json

# Select by index (robust against file encoding issues)
$hrDeptId = $itDepts[1].departmentId
$itDeptId = $itDepts[5].departmentId

Write-Host "HR Dept ID: $hrDeptId"
Write-Host "IT Dept ID: $itDeptId"

# 3. Create a course targeted to HR department only
$uniqueCode = "HR_TEST_" + (Get-Date -Format "yyyyMMddHHmmss")
$courseBody = @{
    courseCode = $uniqueCode
    title = "Khóa học nghiệp vụ HR Đặc biệt"
    description = "Khóa học này chỉ dành riêng cho phòng Nhân sự, phòng IT không được học."
    level = 1
    status = "Published"
    isMandatory = $false
    targetDepartmentIds = @($hrDeptId)
} | ConvertTo-Json

# Convert to UTF-8 bytes to ensure correct model binding in PowerShell 5.1
$bodyBytes = [System.Text.Encoding]::UTF8.GetBytes($courseBody)

Write-Host "Creating course targeted to HR..."
$createRes = Invoke-WebRequest -Uri $createCourseUrl -Method Post -Body $bodyBytes -ContentType "application/json" -WebSession $sessionAdmin -UseBasicParsing -ErrorAction Stop
Write-Host "Course created successfully."

# 4. Verify visibility for IT student (hoanglhit0002)
Write-Host "Checking courses for IT Student..."
$itCourses = Get-StudentCourses "hoanglhit0002"
$hasHrCourseIt = $itCourses | Where-Object { $_.title -eq "Khóa học nghiệp vụ HR Đặc biệt" }
if ($hasHrCourseIt) {
    Write-Host "ERROR: IT Student can see the HR targeted course!" -ForegroundColor Red
} else {
    Write-Host "SUCCESS: IT Student CANNOT see the HR targeted course." -ForegroundColor Green
}

# 5. Verify visibility for HR student (maipthr0002)
Write-Host "Checking courses for HR Student..."
$hrCourses = Get-StudentCourses "maipthr0002"
$hasHrCourseHr = $hrCourses | Where-Object { $_.title -eq "Khóa học nghiệp vụ HR Đặc biệt" }
if ($hasHrCourseHr) {
    Write-Host "SUCCESS: HR Student CAN see the HR targeted course." -ForegroundColor Green
} else {
    Write-Host "ERROR: HR Student CANNOT see the HR targeted course!" -ForegroundColor Red
}
