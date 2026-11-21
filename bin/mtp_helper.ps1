param(
    [string]$Action,
    [string]$DeviceName = "",
    [string]$SubPath = "",
    [string]$SourceFile = "",
    [string]$FileName = "",
    [string]$NewName = "",
    [string]$FileListPath = "",
    [string]$DestDir = ""
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
[Console]::InputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

function Write-MtpLog {
    param([string]$level, [string]$msg)
    try {
        $timestamp = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss.fff")
        $line = "[$timestamp] [PS-$level] $msg"
        
        $baseDir = Split-Path -Parent $PSScriptRoot
        $logDir = Join-Path $baseDir "data"
        if (![System.IO.Directory]::Exists($logDir)) {
            [System.IO.Directory]::CreateDirectory($logDir) | Out-Null
        }
        $logFile = Join-Path $logDir "mtp_debug.log"
        [System.IO.File]::AppendAllText($logFile, "$line`r`n", [System.Text.Encoding]::UTF8)
    } catch {}
}

Write-MtpLog "INFO" "Invoke Action='$Action', DeviceName='$DeviceName', SubPath='$SubPath'"

function Get-MTPDevices {
    $shell = New-Object -ComObject Shell.Application
    $pc = $shell.NameSpace(17)
    $devices = @()

    if (!$pc) {
        Write-MtpLog "ERROR" "Get-MTPDevices: Shell.NameSpace(17) returned null"
        return @()
    }

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
    Write-MtpLog "INFO" "Get-MTPDevices: found $($devices.Count) MTP devices"
    return @($devices)
}

function Detect-MTPGarminWatch {
    $shell = New-Object -ComObject Shell.Application
    $pc = $shell.NameSpace(17)
    $detected = @()

    if (!$pc) {
        Write-MtpLog "ERROR" "Detect-MTPGarminWatch: Shell.NameSpace(17) returned null"
        return @()
    }

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
                    # Level 2: Device\Internal Storage\Music, Device\內部儲存空間\Music, Primary\Music, etc.
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
                    $totalStr = ""
                    $freeStr = ""
                    $usedPctStr = ""
                    try { if ($storageItem) { $totalStr = $f.GetDetailsOf($storageItem, 2) } } catch {}
                    try { if ($storageItem) { $freeStr = $f.GetDetailsOf($storageItem, 3) } } catch {}
                    try { if ($storageItem) { $usedPctStr = $f.GetDetailsOf($storageItem, 4) } } catch {}

                    Write-MtpLog "INFO" "Detect-MTPGarminWatch: Found Garmin watch '$devName' at '$musicPath' (Total: $totalStr, Free: $freeStr)"
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
    Write-MtpLog "INFO" "Detect-MTPGarminWatch: detected $($detected.Count) watch devices"
    return @($detected)
}

function Get-MTPFolderItem {
    param(
        [string]$DevName, 
        [string]$RelPath,
        [switch]$Create
    )
    Write-MtpLog "DEBUG" "Get-MTPFolderItem DevName='$DevName', RelPath='$RelPath', Create=$Create"

    $shell = New-Object -ComObject Shell.Application
    $pc = $shell.NameSpace(17)
    if (!$pc) {
        Write-MtpLog "ERROR" "Failed to get Shell.NameSpace(17)"
        return $null
    }

    # Find device:
    # 1. Exact match
    $device = $pc.Items() | Where-Object { $_.Name.Trim() -eq $DevName.Trim() } | Select-Object -First 1
    # 2. Substring or reverse-substring match
    if (!$device) {
        $device = $pc.Items() | Where-Object { 
            $_.Name.Trim() -like "*$($DevName.Trim())*" -or 
            $DevName.Trim() -like "*$($_.Name.Trim())*" 
        } | Select-Object -First 1
    }
    if (!$device) {
        $avail = ($pc.Items() | ForEach-Object { $_.Name }) -join ", "
        Write-MtpLog "WARN" "Device '$DevName' not found in This PC. Available items: [$avail]"
        return $null
    }

    Write-MtpLog "INFO" "Resolved device item: '$($device.Name)'"

    $folder = $null
    try { $folder = $device.GetFolder } catch {
        Write-MtpLog "ERROR" "device.GetFolder threw: $_"
    }
    if (!$folder) {
        Write-MtpLog "WARN" "device.GetFolder returned null"
        return $null
    }

    if (!$RelPath -or $RelPath.Trim() -eq "") {
        return $folder
    }

    $cleanRel = $RelPath.Trim('\').Replace('/', '\')
    $parts = $cleanRel.Split('\')

    # Try step-by-step navigation
    $currFolder = $folder
    $resolved = $true

    for ($i = 0; $i -lt $parts.Length; $i++) {
        $part = $parts[$i].Trim()
        if (!$part) { continue }

        $child = $null
        try {
            $child = $currFolder.Items() | Where-Object { $_.Name.Trim() -eq $part } | Select-Object -First 1
        } catch {
            Write-MtpLog "ERROR" "currFolder.Items() threw searching for '$part': $_"
        }

        # Case-insensitive / whitespace-tolerant match
        if (!$child) {
            try {
                $child = $currFolder.Items() | Where-Object { 
                    [string]::Equals($_.Name.Trim(), $part, [System.StringComparison]::OrdinalIgnoreCase) 
                } | Select-Object -First 1
            } catch {}
        }

        # If searching for storage folder (e.g. "Internal Storage"), also check common localized aliases:
        if (!$child -and $i -eq 0 -and ($part -in @("Internal Storage", "Primary", "內部儲存空間", "内部存储", "GARMIN", "Phone", "Storage", "內部共用儲存空間"))) {
            Write-MtpLog "DEBUG" "Storage part '$part' not directly matched. Trying storage aliases..."
            try {
                $storageAliases = @("Internal Storage", "Primary", "內部儲存空間", "内部存储", "GARMIN", "Phone", "Storage", "內部共用儲存空間")
                $child = $currFolder.Items() | Where-Object { 
                    $itemName = $_.Name.Trim()
                    ($storageAliases -contains $itemName) -or ($itemName -match "(?i)storage|儲存|存储|primary")
                } | Select-Object -First 1
                if ($child) {
                    Write-MtpLog "INFO" "Storage part '$part' matched alias '$($child.Name)'"
                }
            } catch {}
        }

        # If not found and Create flag is on
        if (!$child -and $Create) {
            try {
                Write-MtpLog "INFO" "Creating folder '$part' in '$($currFolder.Title)'"
                $currFolder.NewFolder($part)
                Start-Sleep -Milliseconds 400
                $child = $currFolder.Items() | Where-Object { [string]::Equals($_.Name.Trim(), $part, [System.StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1
            } catch {
                Write-MtpLog "ERROR" "NewFolder('$part') failed: $_"
            }
        }

        if (!$child) {
            $resolved = $false
            $childrenNames = ($currFolder.Items() | ForEach-Object { $_.Name }) -join ", "
            Write-MtpLog "WARN" "Part '$part' not found in folder '$($currFolder.Title)'. Available children: [$childrenNames]"
            break
        }

        $nextF = $null
        try { $nextF = $child.GetFolder } catch {
            Write-MtpLog "ERROR" "child.GetFolder threw for '$part': $_"
        }
        if (!$nextF) {
            $resolved = $false
            Write-MtpLog "WARN" "child.GetFolder returned null for '$part'"
            break
        }
        $currFolder = $nextF
    }

    if ($resolved -and $currFolder) {
        Write-MtpLog "INFO" "Successfully resolved folder path '$RelPath'"
        return $currFolder
    }

    # FALLBACK FOR MUSIC DIRECTORY:
    # If looking for a path with Music and step-by-step failed, find the Music folder anywhere under device!
    if ($cleanRel -match "(?i)Music") {
        Write-MtpLog "INFO" "Step-by-step resolution failed. Starting fallback search for 'Music' under device..."
        # 1. Directly under device
        try {
            $directMusic = $folder.Items() | Where-Object { $_.Name -like "*music*" -or $_.Name -like "*音樂*" } | Select-Object -First 1
            if ($directMusic) {
                $mF = $directMusic.GetFolder
                if ($mF) {
                    Write-MtpLog "INFO" "Fallback found Music folder directly under device: '$($directMusic.Name)'"
                    return $mF
                }
            }
        } catch {}

        # 2. Check under all level 1 subfolders (e.g. Device -> [Internal Storage / 內部儲存空間 / etc.] -> Music)
        try {
            foreach ($sub in $folder.Items()) {
                $subF = $null
                try { $subF = $sub.GetFolder } catch {}
                if ($subF) {
                    $mItem = $subF.Items() | Where-Object { $_.Name -like "*music*" -or $_.Name -like "*音樂*" } | Select-Object -First 1
                    if ($mItem) {
                        $mF = $null
                        try { $mF = $mItem.GetFolder } catch {}
                        if ($mF) {
                            Write-MtpLog "INFO" "Fallback found Music folder under '$($sub.Name)': '$($mItem.Name)'"
                            return $mF
                        }
                    }
                }
            }
        } catch {
            Write-MtpLog "ERROR" "Fallback search error: $_"
        }
    }

    Write-MtpLog "WARN" "Could not resolve folder '$RelPath' on device '$DevName'"
    return $null
}

function Get-MTPStorageInfo {
    param([string]$DevName, [string]$RelPath)
    Write-MtpLog "DEBUG" "Get-MTPStorageInfo DevName='$DevName', RelPath='$RelPath'"
    $shell = New-Object -ComObject Shell.Application
    $pc = $shell.NameSpace(17)
    if (!$pc) { return $null }

    $device = $pc.Items() | Where-Object { $_.Name.Trim() -eq $DevName.Trim() -or $_.Name -like "*$DevName*" -or $DevName -like "*$($_.Name)*" } | Select-Object -First 1
    if (!$device) { return $null }

    $folder = $null
    try { $folder = $device.GetFolder } catch {}
    if (!$folder) { return $null }

    $parts = if ($RelPath) { @($RelPath.Trim('\').Replace('/', '\').Split('\')) } else { @() }
    $storagePart = if ($parts.Count -gt 0) { [string]$parts[0] } else { "" }
    $storageItem = $null
    if ($storagePart) {
        $storageItem = $folder.Items() | Where-Object { [string]$_.Name.Trim() -eq $storagePart.Trim() } | Select-Object -First 1
    }
    if (!$storageItem) {
        $storageItem = $folder.Items() | Where-Object { 
            $itemName = $_.Name.Trim()
            $itemName -match "(?i)storage|儲存|存储|primary"
        } | Select-Object -First 1
    }
    if (!$storageItem) {
        $storageItem = $folder.Items() | Select-Object -First 1
    }
    if (!$storageItem) { return $null }

    $totalStr = ""
    $freeStr = ""
    $usedPctStr = ""
    try { $totalStr = $folder.GetDetailsOf($storageItem, 2) } catch {}
    try { $freeStr = $folder.GetDetailsOf($storageItem, 3) } catch {}
    try { $usedPctStr = $folder.GetDetailsOf($storageItem, 4) } catch {}

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
        $parts = $RelPath.Trim('\').Replace('/', '\').Split('\')
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
    Write-MtpLog "INFO" "Copy-FileToMTP: DevName='$DevName', RelPath='$RelPath', SrcFile='$SrcFile'"
    if (![System.IO.File]::Exists($SrcFile)) {
        return [PSCustomObject]@{ success = $false; error = "本機來源檔案不存在: $SrcFile" }
    }
    $srcItem = Get-Item $SrcFile
    $srcName = $srcItem.Name
    $srcLength = $srcItem.Length

    $folder = Get-MTPFolderItem -DevName $DevName -RelPath $RelPath -Create
    if (!$folder) {
        Write-MtpLog "ERROR" "Copy-FileToMTP: Failed to find or create destination folder '$RelPath'"
        return [PSCustomObject]@{ success = $false; error = "找不到或無法建立手錶目標資料夾: $RelPath" }
    }

    # If the file already exists on the watch, delete it first to ensure clean copy & detection
    try {
        $existing = $folder.Items() | Where-Object { $_.Name -eq $srcName } | Select-Object -First 1
        if ($existing) {
            Write-MtpLog "INFO" "Copy-FileToMTP: Deleting existing file '$srcName' on watch first"
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
                $detSize = ""
                try { $detSize = $folder.GetDetailsOf($destItem, 2) } catch {}
                # 當 GetDetailsOf 返回非空且非 0 時，代表傳輸已順利完成
                if ($detSize -and $detSize.Trim() -ne "" -and $detSize -ne "0" -and $detSize -ne "0 KB") {
                    $completed = $true
                    Start-Sleep -Milliseconds 500
                    break
                }
            }
        }

        if ($completed) {
            Write-MtpLog "INFO" "Copy-FileToMTP: Successfully copied '$srcName' in $($sw.ElapsedMilliseconds)ms"
            return [PSCustomObject]@{ success = $true }
        }

        # 最後再次確認檔案是否存在
        $finalCheck = $folder.Items() | Where-Object { $_.Name -eq $srcName } | Select-Object -First 1
        if ($finalCheck) {
            Write-MtpLog "INFO" "Copy-FileToMTP: Final check passed for '$srcName'"
            return [PSCustomObject]@{ success = $true }
        } else {
            Write-MtpLog "WARN" "Copy-FileToMTP: Transfer timed out, file '$srcName' not found"
            return [PSCustomObject]@{ success = $false; error = "傳輸逾時，檔案未成功寫入手錶" }
        }
    } catch {
        Write-MtpLog "ERROR" "Copy-FileToMTP exception: $_"
        return [PSCustomObject]@{ success = $false; error = $_.Exception.Message }
    }
}

function Copy-FileFromMTP {
    param([string]$DevName, [string]$RelPath, [string]$FileName, [string]$DestDir)
    try {
        $folder = Get-MTPFolderItem -DevName $DevName -RelPath $RelPath
        if (!$folder) {
            return [PSCustomObject]@{ success = $false; error = "找不到資料夾: $RelPath" }
        }
        $item = $folder.Items() | Where-Object { $_.Name.Trim() -eq $FileName.Trim() -or $_.Name -like "*$FileName*" } | Select-Object -First 1
        if (!$item) {
            return [PSCustomObject]@{ success = $false; error = "找不到檔案: $FileName" }
        }

        $shell = New-Object -ComObject Shell.Application
        $dest = $shell.Namespace($DestDir)
        if (!$dest) {
            return [PSCustomObject]@{ success = $false; error = "本機目標目錄不存在: $DestDir" }
        }

        $dest.CopyHere($item, 16 + 512)
        $destFile = Join-Path $DestDir $item.Name
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        while ($sw.ElapsedMilliseconds -lt 8000) {
            if ([System.IO.File]::Exists($destFile) -and (Get-Item $destFile).Length -gt 0) {
                break
            }
            Start-Sleep -Milliseconds 250
        }
        return [PSCustomObject]@{ success = [System.IO.File]::Exists($destFile); file = $destFile }
    } catch {
        return [PSCustomObject]@{ success = $false; error = $_.Exception.Message }
    }
}

function Delete-FileFromMTP {
    param([string]$DevName, [string]$RelPath, [string]$FileName)
    Write-MtpLog "INFO" "Delete-FileFromMTP DevName='$DevName', RelPath='$RelPath', FileName='$FileName'"
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
        Write-MtpLog "ERROR" "Delete-FileFromMTP error: $_"
        return [PSCustomObject]@{ success = $false; error = $_.Exception.Message }
    }
}

function Delete-FilesBatch {
    param([string]$DevName, [string]$FileListPath)
    Write-MtpLog "INFO" "Delete-FilesBatch DevName='$DevName', FileListPath='$FileListPath'"
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
    Write-MtpLog "INFO" "Rename-FileInMTP DevName='$DevName', RelPath='$RelPath', FileName='$FileName', NewName='$NewName'"
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
    Write-MtpLog "INFO" "List-MTPFiles starting. DevName='$DevName', RelPath='$RelPath'"
    $folder = Get-MTPFolderItem -DevName $DevName -RelPath $RelPath
    if (!$folder) {
        Write-MtpLog "WARN" "List-MTPFiles: folder could not be resolved for DevName='$DevName', RelPath='$RelPath'"
        return @()
    }

    # 遞迴掃描 MTP 目錄下所有音樂與播放清單檔案
    function Recurse-MTPFolder($f, $rel) {
        $result = @()
        $items = $null
        try {
            $items = $f.Items()
        } catch {
            Write-MtpLog "ERROR" "Recurse-MTPFolder failed to get items: $_"
            return @()
        }
        if (!$items) {
            Write-MtpLog "DEBUG" "Recurse-MTPFolder: folder '$rel' returned null Items()"
            return @()
        }

        # 檢索所有項目（相容 Windows 10 COM IEnumVARIANT 與 index 索引方式）
        $itemList = @()
        $cnt = 0
        try { $cnt = $items.Count } catch {}
        Write-MtpLog "DEBUG" "Recurse-MTPFolder: folder '$rel' reports $cnt items"

        if ($cnt -gt 0) {
            for ($i = 0; $i -lt $cnt; $i++) {
                try {
                    $it = $items.Item($i)
                    if ($it) { $itemList += $it }
                } catch {}
            }
        }

        if ($itemList.Count -eq 0) {
            try {
                foreach ($it in $items) {
                    if ($it) { $itemList += $it }
                }
            } catch {
                Write-MtpLog "WARN" "Recurse-MTPFolder: foreach loop threw: $_"
            }
        }

        Write-MtpLog "DEBUG" "Recurse-MTPFolder: folder '$rel' retrieved $($itemList.Count) items to inspect"

        foreach ($item in $itemList) {
            try {
                $isFolder = $false
                try { $isFolder = $item.IsFolder } catch {}

                $itemName = ""
                try { $itemName = [string]$item.Name } catch {}
                $itemType = ""
                try { $itemType = [string]$item.Type } catch {}
                $itemPath = ""
                try { $itemPath = [string]$item.Path } catch {}

                if ($isFolder) {
                    $subF = $null
                    try { $subF = $item.GetFolder } catch {}
                    if ($subF) {
                        $nextRel = if ($rel) { "$rel\$itemName" } else { $itemName }
                        $subResults = Recurse-MTPFolder $subF $nextRel
                        if ($subResults) { $result += $subResults }
                    }
                } else {
                    if ([string]::IsNullOrWhiteSpace($itemName)) { continue }

                    # 1. 從檔名取得副檔名
                    $ext = [System.IO.Path]::GetExtension($itemName).ToLowerInvariant()

                    # 2. 若系統隱藏已知副檔名，嘗試從虛擬路徑取得副檔名
                    if ([string]::IsNullOrWhiteSpace($ext) -and !([string]::IsNullOrWhiteSpace($itemPath))) {
                        $ext = [System.IO.Path]::GetExtension($itemPath).ToLowerInvariant()
                    }

                    # 3. 若仍無副檔名，從 Windows Shell 檔案類型判斷（例如 "MP3 音訊", "Audio", "FLAC", "播放清單"）
                    $isAudioByType = $false
                    if ([string]::IsNullOrWhiteSpace($ext) -and !([string]::IsNullOrWhiteSpace($itemType))) {
                        if ($itemType -match "(?i)MP3|M4A|AAC|FLAC|WAV|WMA|OGG|Audio|音訊|音频|聲音|声音") {
                            $isAudioByType = $true
                            $ext = ".mp3"
                        } elseif ($itemType -match "(?i)Playlist|播放清單|播放列表") {
                            $isAudioByType = $true
                            $ext = ".m3u8"
                        }
                    }

                    $isAudioOrPlaylist = ($ext -in @('.mp3', '.m4a', '.aac', '.flac', '.wav', '.wma', '.ogg', '.m3u', '.m3u8')) -or $isAudioByType

                    if ($isAudioOrPlaylist) {
                        $sizeStr = ""
                        try { $sizeStr = [string]$f.GetDetailsOf($item, 2) } catch {}

                        $fileSize = 0
                        try {
                            if ($item.Size) { $fileSize = [int64]$item.Size }
                        } catch {}

                        $isPlaylist = ($ext -eq '.m3u' -or $ext -eq '.m3u8')
                        $folderName = if ($isPlaylist) { "手機撥放清單" } elseif ($rel) { $rel.Split('\')[0] } else { "手機撥放清單" }
                        $fileRel = if ($rel) { "$rel\$itemName" } else { $itemName }

                        $result += [PSCustomObject]@{
                            name        = $itemName
                            folder      = $folderName
                            sub_folder  = $folderName
                            rel_path    = $fileRel
                            size_str    = $sizeStr
                            size        = $fileSize
                            path        = "mtp:\$DevName\$RelPath\$fileRel"
                        }
                        Write-MtpLog "INFO" "Matched audio item: '$itemName' (ext: '$ext', type: '$itemType', size: '$sizeStr')"
                    } else {
                        Write-MtpLog "DEBUG" "Skipped item: '$itemName' (ext: '$ext', type: '$itemType')"
                    }
                }
            } catch {
                Write-MtpLog "WARN" "Error processing item in folder '$rel': $_"
            }
        }
        return @($result)
    }

    $allFiles = @(Recurse-MTPFolder $folder "")
    
    # 若在預設目錄下找為 0 首，自動嘗試 Garmin 常見備援目錄
    if ($allFiles.Count -eq 0) {
        Write-MtpLog "WARN" "List-MTPFiles: 0 files found in '$RelPath'. Checking alternate Garmin locations on '$DevName'..."
        $candidates = @("Internal Storage\GARMIN\Music", "GARMIN\Music", "Internal Storage\Audio", "內部儲存空間\GARMIN\Music", "內部儲存空間\Audio")
        foreach ($alt in $candidates) {
            if ($alt -eq $RelPath) { continue }
            $altFolder = Get-MTPFolderItem -DevName $DevName -RelPath $alt
            if ($altFolder) {
                Write-MtpLog "INFO" "List-MTPFiles: Trying alternate location '$alt'..."
                $altFiles = @(Recurse-MTPFolder $altFolder "")
                if ($altFiles.Count -gt 0) {
                    Write-MtpLog "INFO" "List-MTPFiles: Found $($altFiles.Count) files in '$alt'!"
                    $allFiles = $altFiles
                    break
                }
            }
        }
    }

    Write-MtpLog "INFO" "List-MTPFiles complete. Total audio/playlist files found: $($allFiles.Count)"
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
elseif ($Action -eq "copy_from_mtp") {
    $res = Copy-FileFromMTP -DevName $DeviceName -RelPath $SubPath -FileName $FileName -DestDir $DestDir
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
