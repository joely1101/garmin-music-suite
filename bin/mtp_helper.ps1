param(
    [string]$Action,
    [string]$DeviceName = "",
    [string]$SubPath = "",
    [string]$SourceFile = "",
    [string]$FileName = "",
    [string]$NewName = ""
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

function Get-MTPDevices {
    $shell = New-Object -ComObject Shell.Application
    $pc = $shell.NameSpace(17)
    $devices = @()

    foreach ($item in $pc.Items()) {
        $isDrive = $item.Path -match '^[A-Za-z]:\\'
        if (-not $isDrive) {
            $f = $null
            try { $f = $item.GetFolder } catch {}
            if ($f -ne $null -or $item.IsFolder) {
                $devices += [PSCustomObject]@{
                    name   = $item.Name
                    path   = "mtp:\" + $item.Name
                    label  = $item.Name + " (MTP 裝置)"
                    is_mtp = $true
                }
            }
        }
    }
    return @($devices)
}

function Detect-MTPGarminWatch {
    $shell = New-Object -ComObject Shell.Application
    $pc = $shell.NameSpace(17)
    $detected = @()

    foreach ($item in $pc.Items()) {
        $isDrive = $item.Path -match '^[A-Za-z]:\\'
        if (-not $isDrive) {
            $devName = $item.Name
            $f = $null
            try { $f = $item.GetFolder } catch {}
            if ($f) {
                $musicPath = $null
                $storageItem = $null

                # Level 1: Device\Music
                foreach ($sub1 in $f.Items()) {
                    if ($sub1.Name -eq "Music" -or $sub1.Name -eq "music") {
                        $musicPath = "mtp:\$devName\$($sub1.Name)"
                        $storageItem = $sub1
                        break
                    }
                    # Level 2: Device\Internal Storage\Music, Device\Primary\Music, etc.
                    $subF = $null
                    try { $subF = $sub1.GetFolder } catch {}
                    if ($subF) {
                        foreach ($sub2 in $subF.Items()) {
                            if ($sub2.Name -eq "Music" -or $sub2.Name -eq "music") {
                                $musicPath = "mtp:\$devName\$($sub1.Name)\$($sub2.Name)"
                                $storageItem = $sub1
                                break
                            }
                        }
                    }
                    if ($musicPath) { break }
                }

                if ($musicPath) {
                    $totalStr = if ($storageItem) { $f.GetDetailsOf($storageItem, 2) } else { "" }
                    $freeStr = if ($storageItem) { $f.GetDetailsOf($storageItem, 3) } else { "" }
                    $usedPctStr = if ($storageItem) { $f.GetDetailsOf($storageItem, 4) } else { "" }

                    $detected += [PSCustomObject]@{
                        name        = $devName
                        path        = $musicPath
                        label       = "Garmin 手錶 ($devName)"
                        total_str   = $totalStr
                        free_str    = $freeStr
                        used_pct    = $usedPctStr
                        is_mtp      = $true
                    }
                }
            }
        }
    }
    return @($detected)
}

function Get-MTPFolderItem {
    param(
        [string]$DevName, 
        [string]$RelPath,
        [switch]$Create
    )
    $shell = New-Object -ComObject Shell.Application
    $pc = $shell.NameSpace(17)
    $device = $pc.Items() | Where-Object { $_.Name.Trim() -eq $DevName.Trim() } | Select-Object -First 1
    if (!$device) {
        $device = $pc.Items() | Where-Object { $_.Name -like "*$DevName*" } | Select-Object -First 1
    }
    if (!$device) { return $null }

    $folder = $null
    try { $folder = $device.GetFolder } catch {}
    if (!$folder) { return $null }

    if (!$RelPath -or $RelPath.Trim() -eq "") {
        return $folder
    }

    $parts = $RelPath.Trim('\').Split('\')
    foreach ($part in $parts) {
        if (!$part) { continue }
        $child = $folder.Items() | Where-Object { $_.Name.Trim() -eq $part.Trim() } | Select-Object -First 1
        if (!$child -and $Create) {
            try {
                $folder.NewFolder($part.Trim())
                Start-Sleep -Milliseconds 350
                $child = $folder.Items() | Where-Object { $_.Name.Trim() -eq $part.Trim() } | Select-Object -First 1
            } catch {}
        }
        if (!$child) { return $null }
        $folder = $child.GetFolder
    }
    return $folder
}

function Get-MTPStorageInfo {
    param([string]$DevName, [string]$RelPath)
    $shell = New-Object -ComObject Shell.Application
    $pc = $shell.NameSpace(17)
    $device = $pc.Items() | Where-Object { $_.Name.Trim() -eq $DevName.Trim() -or $_.Name -like "*$DevName*" } | Select-Object -First 1
    if (!$device) { return $null }

    $folder = $device.GetFolder
    if (!$folder) { return $null }

    $parts = if ($RelPath) { @($RelPath.Trim('\').Split('\')) } else { @() }
    $storagePart = if ($parts.Count -gt 0) { [string]$parts[0] } else { "" }
    $storageItem = $null
    if ($storagePart) {
        $storageItem = $folder.Items() | Where-Object { [string]$_.Name.Trim() -eq $storagePart.Trim() } | Select-Object -First 1
    }
    if (!$storageItem) {
        $storageItem = $folder.Items() | Select-Object -First 1
    }
    if (!$storageItem) { return $null }

    $totalStr = $folder.GetDetailsOf($storageItem, 2)
    $freeStr = $folder.GetDetailsOf($storageItem, 3)
    $usedPctStr = $folder.GetDetailsOf($storageItem, 4)

    return [PSCustomObject]@{
        total_str = $totalStr
        free_str  = $freeStr
        used_pct  = $usedPctStr
    }
}

function List-MTPSubfolders {
    param([string]$DevName, [string]$RelPath)
    $folder = Get-MTPFolderItem -DevName $DevName -RelPath $RelPath
    if (!$folder) {
        return [PSCustomObject]@{ error = "找不到指定的 MTP 裝置或資料夾 ($DevName)" }
    }

    $subfolders = @()
    foreach ($item in $folder.Items()) {
        $f = $null
        try { $f = $item.GetFolder } catch {}
        if ($f -ne $null -or $item.IsFolder) {
            $childRel = if ($RelPath) { "$RelPath\$($item.Name)" } else { $item.Name }
            $subfolders += [PSCustomObject]@{
                name = $item.Name
                path = "mtp:\$DevName\$childRel"
            }
        }
    }

    $parentPath = $null
    if ($RelPath) {
        $parts = $RelPath.Trim('\').Split('\')
        if ($parts.Count -gt 1) {
            $parentRel = ($parts[0..($parts.Count - 2)]) -join '\'
            $parentPath = "mtp:\$DevName\$parentRel"
        } else {
            $parentPath = "mtp:\$DevName"
        }
    }

    $curPath = if ($RelPath) { "mtp:\$DevName\$RelPath" } else { "mtp:\$DevName" }

    return [PSCustomObject]@{
        current_path = $curPath
        parent_path  = $parentPath
        subfolders   = @($subfolders)
    }
}

function Copy-FileToMTP {
    param([string]$DevName, [string]$RelPath, [string]$SrcFile)
    if (!(Test-Path $SrcFile)) {
        return [PSCustomObject]@{ success = $false; error = "本機來源檔案不存在: $SrcFile" }
    }

    $folder = Get-MTPFolderItem -DevName $DevName -RelPath $RelPath -Create
    if (!$folder) {
        return [PSCustomObject]@{ success = $false; error = "找不到或無法建立目標資料夾: $RelPath" }
    }

    $srcFileName = [System.IO.Path]::GetFileName($SrcFile)
    $srcItem = Get-Item $SrcFile
    $srcSize = $srcItem.Length

    # If the file already exists on the watch, delete it first to ensure clean copy & detection
    try {
        $existing = $folder.Items() | Where-Object { $_.Name -eq $srcFileName } | Select-Object -First 1
        if ($existing) {
            $existing.InvokeVerb("delete")
            Start-Sleep -Milliseconds 500
        }
    } catch {}

    try {
        # CopyHere with options: 16 (Yes to all) + 512 (Do not confirm directory creation)
        $folder.CopyHere($SrcFile, 16 + 512)
    } catch {
        return [PSCustomObject]@{ success = $false; error = $_.Exception.Message }
    }

    # Asynchronous completion polling
    # Shell.Application CopyHere transfers in the background. We must wait until the file appears and stabilizes.
    $timeoutSeconds = [Math]::Max(45, [int]($srcSize / (1024 * 1024)) * 3 + 15)
    $start = [DateTime]::UtcNow
    $found = $null
    $lastSizeStr = ""
    $stableCount = 0

    while (([DateTime]::UtcNow - $start).TotalSeconds -lt $timeoutSeconds) {
        Start-Sleep -Milliseconds 800
        $found = $folder.Items() | Where-Object { $_.Name -eq $srcFileName } | Select-Object -First 1
        if ($found) {
            $curSizeStr = $folder.GetDetailsOf($found, 2)
            if ($curSizeStr -and $curSizeStr -eq $lastSizeStr) {
                $stableCount++
                if ($stableCount -ge 2) {
                    # Transfer complete and stabilized!
                    break
                }
            } else {
                $lastSizeStr = $curSizeStr
                $stableCount = 0
            }
        }
    }

    if ($found) {
        # Ensure file handle release
        Start-Sleep -Milliseconds 500
        return [PSCustomObject]@{ success = $true }
    } else {
        return [PSCustomObject]@{ success = $false; error = "檔案傳輸逾時，目標資料夾未在時間內完成寫入 ($srcFileName)" }
    }
}

function Delete-FileFromMTP {
    param([string]$DevName, [string]$RelPath, [string]$FileName)
    $folder = Get-MTPFolderItem -DevName $DevName -RelPath $RelPath
    if (!$folder) {
        return [PSCustomObject]@{ success = $false; error = "找不到目標資料夾: $RelPath" }
    }
    $item = $folder.Items() | Where-Object { $_.Name -eq $FileName } | Select-Object -First 1
    if (!$item) {
        return [PSCustomObject]@{ success = $false; error = "找不到檔案: $FileName" }
    }
    try {
        $item.InvokeVerb("delete")
        return [PSCustomObject]@{ success = $true }
    } catch {
        return [PSCustomObject]@{ success = $false; error = $_.Exception.Message }
    }
}

function Rename-FileInMTP {
    param([string]$DevName, [string]$RelPath, [string]$FileName, [string]$NewName)
    $folder = Get-MTPFolderItem -DevName $DevName -RelPath $RelPath
    if (!$folder) {
        return [PSCustomObject]@{ success = $false; error = "找不到目標資料夾: $RelPath" }
    }
    $item = $folder.Items() | Where-Object { $_.Name -eq $FileName } | Select-Object -First 1
    if (!$item) {
        return [PSCustomObject]@{ success = $false; error = "找不到檔案: $FileName" }
    }
    try {
        $item.Name = $NewName
        return [PSCustomObject]@{ success = $true }
    } catch {
        return [PSCustomObject]@{ success = $false; error = $_.Exception.Message }
    }
}

function List-MTPFiles {
    param([string]$DevName, [string]$RelPath)
    $folder = Get-MTPFolderItem -DevName $DevName -RelPath $RelPath
    if (!$folder) {
        return @()
    }
    
    $fileList = New-Object System.Collections.ArrayList

    function Recurse-Folder($curFolder, $currentRel) {
        foreach ($item in $curFolder.Items()) {
            if ($item.IsFolder) {
                $subF = $null
                try { $subF = $item.GetFolder } catch {}
                if ($subF) {
                    $subRel = if ($currentRel) { "$currentRel\$($item.Name)" } else { $item.Name }
                    Recurse-Folder $subF $subRel
                }
            } else {
                $name = $item.Name
                $ext = [System.IO.Path]::GetExtension($name).ToLower()
                if ($ext -in @('.mp3', '.m4a', '.aac', '.flac', '.wav', '.wma', '.ogg')) {
                    $itemRel = if ($currentRel) { "$currentRel\$name" } else { $name }
                    $fullPath = if ($RelPath) { "mtp:\$DevName\$RelPath\$itemRel" } else { "mtp:\$DevName\$itemRel" }
                    
                    $sizeVal = 0
                    try { $sizeVal = [int64]$item.Size } catch {}
                    $sizeStr = ""
                    try { $sizeStr = $curFolder.GetDetailsOf($item, 2) } catch {}
                    
                    $title = ""
                    try { $title = $curFolder.GetDetailsOf($item, 8) } catch {}
                    $artist = ""
                    try { $artist = $curFolder.GetDetailsOf($item, 9) } catch {}
                    $duration = ""
                    try { $duration = $curFolder.GetDetailsOf($item, 13) } catch {}

                    $folderName = if ($currentRel) {
                        $parts = $currentRel.Split('\')
                        $parts[0]
                    } else { "音樂" }

                    $obj = [PSCustomObject]@{
                        name        = $name
                        path        = $fullPath
                        rel_path    = $itemRel
                        sub_folder  = $folderName
                        size        = $sizeVal
                        size_str    = $sizeStr
                        title       = $title
                        artist      = $artist
                        duration    = $duration
                    }
                    [void]$fileList.Add($obj)
                }
            }
        }
    }

    Recurse-Folder $folder ""
    return @($fileList)
}

if ($Action -eq "list_devices") {
    $devs = @(Get-MTPDevices)
    if ($devs.Count -gt 0) {
        ConvertTo-Json -InputObject $devs -Depth 5 -Compress
    } else {
        Write-Output "[]"
    }
}
elseif ($Action -eq "detect_garmin") {
    $devs = @(Detect-MTPGarminWatch)
    if ($devs.Count -gt 0) {
        ConvertTo-Json -InputObject $devs -Depth 5 -Compress
    } else {
        Write-Output "[]"
    }
}
elseif ($Action -eq "get_storage") {
    $res = Get-MTPStorageInfo -DevName $DeviceName -RelPath $SubPath
    if ($res) {
        $res | ConvertTo-Json -Depth 5 -Compress
    } else {
        Write-Output "{}"
    }
}
elseif ($Action -eq "list_folders") {
    $res = List-MTPSubfolders -DevName $DeviceName -RelPath $SubPath
    if ($res) {
        $res | ConvertTo-Json -Depth 5 -Compress
    } else {
        Write-Output "{}"
    }
}
elseif ($Action -eq "copy_file") {
    $res = Copy-FileToMTP -DevName $DeviceName -RelPath $SubPath -SrcFile $SourceFile
    $res | ConvertTo-Json -Compress
}
elseif ($Action -eq "delete_file") {
    $res = Delete-FileFromMTP -DevName $DeviceName -RelPath $SubPath -FileName $FileName
    $res | ConvertTo-Json -Compress
}
elseif ($Action -eq "rename_file") {
    $res = Rename-FileInMTP -DevName $DeviceName -RelPath $SubPath -FileName $FileName -NewName $NewName
    $res | ConvertTo-Json -Compress
}
elseif ($Action -eq "list_files") {
    $res = @(List-MTPFiles -DevName $DeviceName -RelPath $SubPath)
    if ($res.Count -gt 0) {
        ConvertTo-Json -InputObject $res -Depth 5 -Compress
    } else {
        Write-Output "[]"
    }
}
