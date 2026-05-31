Add-Type -AssemblyName "System.Data"
$conn = New-Object System.Data.SqlClient.SqlConnection
$conn.ConnectionString = "Server=s103-d186.interdata.vn;Database=daotaonoibo;User Id=HieuDao;Password=DuyHieu@662008;Encrypt=True;TrustServerCertificate=True;"
$conn.Open()
$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT AttachmentID, LessonID, FileName, FilePath FROM LessonAttachments WHERE LessonID = 35 OR AttachmentID = 10"
$reader = $cmd.ExecuteReader()
while($reader.Read()) {
    $id = $reader["AttachmentID"]
    $lessonId = $reader["LessonID"]
    $name = $reader["FileName"]
    $path = $reader["FilePath"]
    Write-Host "ID: $id | LessonID: $lessonId | Name: $name | Path: $path"
}
$conn.Close()
