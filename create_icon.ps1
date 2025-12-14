$pngPath = "c:\projects\hotcpu\Images\AppIcon.png"
$icoPath = "c:\projects\hotcpu\Images\AppIcon.ico"

$pngBytes = [System.IO.File]::ReadAllBytes($pngPath)

$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($fs)

# Header
$bw.Write([byte]0)   # Reserved
$bw.Write([byte]0)
$bw.Write([short]1)  # Type = 1 (Icon)
$bw.Write([short]1)  # Count = 1

# Entry
$bw.Write([byte]0)   # Width (0=256)
$bw.Write([byte]0)   # Height (0=256)
$bw.Write([byte]0)   # ColorCount
$bw.Write([byte]0)   # Reserved
$bw.Write([short]1)  # Planes
$bw.Write([short]32) # BitCount
$bw.Write([int]$pngBytes.Length) # Bytes in resource
$bw.Write([int]22)   # Offset (6 + 16)

# Image Data
$bw.Write($pngBytes)

$bw.Close()
$fs.Close()

Write-Host "Created $icoPath"
