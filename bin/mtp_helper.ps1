param(
    [string]$Action,
    [string]$DeviceName = "",
    [string]$SubPath = "",
    [string]$SourceFile = "",
    [string]$FileName = "",
    [string]$NewName = "",
    [string]$FileListPath = ""
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
[Console]::InputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

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
    if (![System.IO.File]::Exists($SrcFile)) {
        return [PSCustomObject]@{ success = $false; error = "本機來源檔案不存在: $SrcFile" }
    }
    $srcItem = Get-Item $SrcFile
    $srcName = $srcItem.Name
    $srcLength = $srcItem.Length

    $folder = Get-MTPFolderItem -DevName $DevName -RelPath $RelPath -Create
    if (!$folder) {
        return [PSCustomObject]@{ success = $false; error = "找不到或無法建立手錶目標資料夾: $RelPath" }
    }

    # If the file already exists on the watch, delete it first to ensure clean copy & detection
    try {
        $existing = $folder.Items() | Where-Object { $_.Name -eq $srcName } | Select-Object -First 1
        if ($existing) {
            $existing.InvokeVerb("delete")
            Start-Sleep -Milliseconds 500
        }
    } catch {}

    try {
        # 16 = FOF_NOCONFIRMATION, 512 = FOF_NOERRORUI
        $folder.CopyHere($SrcFile, 16 + 512)

        # 由於 Windows Shell 對 MTP 的 CopyHere 是非同步執行，
        # 若 PowerShell 程序立即結束，Explorer 將中斷傳輸。
        # 因此在此等候傳輸完成（輪詢檔案是否在目標目錄中完成寫入）
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        # 根據檔案大小動態計算等候時間：至少 45 秒，每 10MB 增加 5 秒
        $maxWaitMs = [Math]::Max(45000, [int]($srcLength / (10 * 1024 * 1024)) * 5000 + 35000)
        $completed = $false

        while ($sw.ElapsedMilliseconds -lt $maxWaitMs) {
            Start-Sleep -Milliseconds 800
            $destItem = $folder.Items() | Where-Object { $_.Name -eq $srcName } | Select-Object -First 1
            if ($destItem) {
                $detSize = $folder.GetDetailsOf($destItem, 2)
                # 當 GetDetailsOf 返回非空且非 0 時，代表傳輸已順利完成
                if ($detSize -and $detSize.Trim() -ne "" -and $detSize -ne "0" -and $detSize -ne "0 KB") {
                    $completed = $true
                    Start-Sleep -Milliseconds 500
                    break
                }
            }
        }

        if ($completed) {
            return [PSCustomObject]@{ success = $true }
        }

        # 最後再次確認檔案是否存在
        $finalCheck = $folder.Items() | Where-Object { $_.Name -eq $srcName } | Select-Object -First 1
        if ($finalCheck) {
            return [PSCustomObject]@{ success = $true }
        } else {
            return [PSCustomObject]@{ success = $false; error = "傳輸逾時，檔案未成功寫入手錶" }
        }
    } catch {
        return [PSCustomObject]@{ success = $false; error = $_.Exception.Message }
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

function Delete-FilesBatch {
    param([string]$DevName, [string]$FileListPath)
    if (!(Test-Path $FileListPath)) {
        return [PSCustomObject]@{ success = $false; error = "找不到檔案清單: $FileListPath" }
    }
    
    $json = [System.IO.File]::ReadAllText($FileListPath, [System.Text.Encoding]::UTF8)
    $files = $json | ConvertFrom-Json
    $results = @()
    $folderCache = @{}

    foreach ($itemInfo in $files) {
        $sub = $itemInfo.SubPath
        $name = $itemInfo.FileName
        if (!$folderCache.ContainsKey($sub)) {
            $folderCache[$sub] = Get-MTPFolderItem -DevName $DevName -RelPath $sub
        }
        $folder = $folderCache[$sub]
        if ($folder) {
            $item = $folder.Items() | Where-Object { $_.Name -eq $name } | Select-Object -First 1
            if ($item) {
                try {
                    $item.InvokeVerb("delete")
                    $results += [PSCustomObject]@{ name = $name; success = $true }
                } catch {
                    $results += [PSCustomObject]@{ name = $name; success = $false; error = $_.Exception.Message }
                }
            } else {
                $results += [PSCustomObject]@{ name = $name; success = $false; error = "找不到檔案" }
            }
        } else {
            $results += [PSCustomObject]@{ name = $name; success = $false; error = "找不到資料夾" }
        }
    }
    
    return [PSCustomObject]@{ success = $true; total = $files.Count; deleted = ($results | Where-Object { $_.success }).Count }
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

    # 遞迴掃描 MTP 目錄下所有音樂與播放清單檔案
    function Recurse-MTPFolder($f, $rel) {
        $result = @()
        foreach ($item in $f.Items()) {
            if ($item.IsFolder) {
                $subF = $null
                try { $subF = $item.GetFolder } catch {}
                if ($subF) {
                    $nextRel = if ($rel) { "$rel\$($item.Name)" } else { $item.Name }
                    $result += Recurse-MTPFolder $subF $nextRel
                }
            } else {
                $name = $item.Name
                $ext = [System.IO.Path]::GetExtension($name).ToLower()
                if ($ext -in @('.mp3', '.m4a', '.aac', '.flac', '.wav', '.wma', '.ogg', '.m3u', '.m3u8')) {
                    $sizeStr = $f.GetDetailsOf($item, 2)
                    $folderName = if ($rel) { $rel.Split('\')[0] } else { "手錶音樂" }
                    $fileRel = if ($rel) { "$rel\$name" } else { $name }
                    $result += [PSCustomObject]@{
                        name        = $name
                        folder      = $folderName
                        sub_folder  = $folderName
                        rel_path    = $fileRel
                        size_str    = $sizeStr
                        size        = $item.Size
                        path        = "mtp:\$DevName\$RelPath\$fileRel"
                    }
                }
            }
        }
        return @($result)
    }

    $allFiles = @(Recurse-MTPFolder $folder "")
    return @($allFiles)
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
elseif ($Action -eq "delete_files") {
    $res = Delete-FilesBatch -DevName $DeviceName -FileListPath $FileListPath
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
