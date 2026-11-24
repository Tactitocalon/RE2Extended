[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$BlocksMap,
    [string[]]$RequireName = @(
        'CruiseAmplifierCV',
        'CruiseAmplifierSV',
        'VoiceInterface',
        'VoiceInterfaceBlocks'
    )
)

$ErrorActionPreference = 'Stop'
$BlocksMap = [IO.Path]::GetFullPath($BlocksMap)
if (!(Test-Path -LiteralPath $BlocksMap)) { throw "blocksmap.dat not found: $BlocksMap" }

$bytes = [IO.File]::ReadAllBytes($BlocksMap)
if ($bytes.Length -lt 7) { throw "blocksmap.dat is too short: $($bytes.Length) bytes" }

$magic = [BitConverter]::ToUInt32($bytes, 0)
if ($magic -ne 0x07891932) { throw ("Unexpected blocksmap magic: 0x{0:X8}" -f $magic) }
if ($bytes[4] -ne 1) { throw "Unsupported blocksmap version: $($bytes[4])" }

$count = [BitConverter]::ToUInt16($bytes, 5)
$offset = 7
$records = New-Object 'System.Collections.Generic.List[object]'
$byName = @{}
$byId = @{}

for ($i = 0; $i -lt $count; $i++) {
    if ($offset + 3 -gt $bytes.Length) { throw "Truncated map at record $i (offset $offset)" }
    $id = [BitConverter]::ToUInt16($bytes, $offset)
    $offset += 2
    $length = [int]$bytes[$offset]
    $offset++
    if ($offset + $length -gt $bytes.Length) { throw "Truncated map at record $i name (offset $offset)" }

    $name = [Text.Encoding]::ASCII.GetString($bytes, $offset, $length)
    $offset += $length

    $records.Add([pscustomobject]@{ Name = $name; Id = $id })
    if ($byName.ContainsKey($name)) { throw "Duplicate block name in map: $name" }
    if ($byId.ContainsKey($id)) { throw "Duplicate block id $id ($($byId[$id]) and $name)" }
    $byName[$name] = $id
    $byId[$id] = $name
}

$nextId = $null
if ($offset + 2 -eq $bytes.Length) {
    $nextId = [BitConverter]::ToUInt16($bytes, $offset)
    $offset += 2
}
elseif ($offset -ne $bytes.Length) {
    Write-Warning "Trailing bytes after $count records: $($bytes.Length - $offset)"
}

$maxId = 0
foreach ($record in $records) { if ($record.Id -gt $maxId) { $maxId = $record.Id } }

Write-Output "BlocksMap=$BlocksMap"
if ($null -eq $nextId) {
    Write-Output "Version=$($bytes[4]) Count=$count MaxId=$maxId"
}
else {
    Write-Output "Version=$($bytes[4]) Count=$count MaxId=$maxId NextId=$nextId"
}

$missing = New-Object 'System.Collections.Generic.List[string]'
foreach ($name in $RequireName) {
    if ($byName.ContainsKey($name)) {
        Write-Output ("FOUND {0} = {1}" -f $name, $byName[$name])
    }
    else {
        Write-Output ("MISSING {0}" -f $name)
        $missing.Add($name)
    }
}

if ($missing.Count -gt 0) { exit 2 }
exit 0
