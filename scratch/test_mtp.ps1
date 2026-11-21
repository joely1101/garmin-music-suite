$shell = New-Object -ComObject Shell.Application
$pc = $shell.NameSpace(17)
Write-Output "Items in NameSpace(17):"
foreach ($item in $pc.Items()) {
    Write-Output " - $($item.Name) [Path: $($item.Path)]"
}
