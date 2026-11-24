[CmdletBinding()]
param(
    [string]$GameRoot = 'F:\SteamLibrary\steamapps\common\Empyrion - Galactic Survival'
)

$ErrorActionPreference = 'Stop'
$Root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '.'))
$RepoRoot = [IO.Path]::GetFullPath((Join-Path $Root '..\..'))
$RuntimeProject = Join-Path $Root 'src\RE2Extended.Runtime\RE2Extended.Runtime.csproj'
$BuildOutput = Join-Path $Root 'artifacts\build'
$Stage = Join-Path $Root 'package\RE2Extended'
$StageRuntime = Join-Path $Stage 'runtime'

if (!(Test-Path (Join-Path $GameRoot 'Client\Empyrion_Data\Managed\ModApi.dll'))) {
    throw "Game installation not found at: $GameRoot"
}

& dotnet restore $RuntimeProject --nologo
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed' }

& dotnet build $RuntimeProject -c Release --nologo --no-restore "-p:GameRoot=$GameRoot"
if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed' }

$HarmonyBase = Join-Path $env:USERPROFILE '.nuget\packages\lib.harmony\2.4.3\lib\net452\0Harmony.dll'
$HarmonyPatched = Join-Path $Root 'artifacts\harmony-fieldtypefix\0Harmony.dll'
if (!(Test-Path $HarmonyBase)) { throw 'Lib.Harmony 2.4.3 net452 input missing' }

& (Join-Path $PSScriptRoot 'scripts\patch-harmony-fieldtype.ps1') `
    -BaseHarmony $HarmonyBase `
    -CecilPath (Join-Path $GameRoot 'Client\Empyrion_Data\Managed\Trivial.Mono.Cecil.dll') `
    -OutputPath $HarmonyPatched
if ($LASTEXITCODE -ne 0 -or !(Test-Path $HarmonyPatched)) { throw 'Harmony field-type patch failed' }

if (Test-Path $Stage) { Remove-Item -LiteralPath $Stage -Recurse -Force }
New-Item -ItemType Directory -Path $StageRuntime -Force | Out-Null

Copy-Item (Join-Path $BuildOutput 'RE2Extended.dll') $Stage -Force
Copy-Item (Join-Path $BuildOutput 'RE2Extended.Runtime.dll') $StageRuntime -Force
Copy-Item $HarmonyPatched (Join-Path $StageRuntime '0Harmony.dll') -Force
Copy-Item (Join-Path $PSScriptRoot 'package\RE2Extended_Info.yaml') $Stage -Force
Copy-Item (Join-Path $PSScriptRoot 'Configuration') $Stage -Recurse -Force

# The Voice Interface PoC uses the requested local third-party WAV. It is copied
# only into the ignored staged package, never into tracked source.
$VoiceAudioStage = Join-Path $Stage 'Audio'
$VoiceAudioFiles = @(
    'voice_critical_hit.wav',
    'voice_jet_engine_destroyed.wav',
    'voice_turret_unit_destroyed.wav',
    'voice_weapon_destroyed.wav',
    'voice_power_unit_destroyed.wav',
    'alarm_critical_damage.wav'
)
foreach ($VoiceAudioFile in $VoiceAudioFiles) {
    $VoiceAudio = Join-Path $RepoRoot (Join-Path 'third-party\SoundWork\final' $VoiceAudioFile)
    if (Test-Path $VoiceAudio) {
        New-Item -ItemType Directory -Path $VoiceAudioStage -Force | Out-Null
        Copy-Item $VoiceAudio (Join-Path $VoiceAudioStage $VoiceAudioFile) -Force
        Write-Output "Staged Voice Interface audio: $VoiceAudio"
    }
    else {
        Write-Warning "Voice Interface audio not found: $VoiceAudio"
    }
}

$HarmonyLicense = Join-Path $env:USERPROFILE '.nuget\packages\lib.harmony\2.4.3\LICENSE'
if (Test-Path $HarmonyLicense) {
    Copy-Item $HarmonyLicense (Join-Path $StageRuntime 'Harmony-LICENSE.txt') -Force
}

$notice = @'
LOCAL MODIFICATION NOTICE
Base: Lib.Harmony 2.4.3 net452 (MIT)
Local change: MonoMod.Utils.Extensions.Is(MemberReference, MemberInfo) now compares
FieldType for nongeneric declaring types after declaring-type/name/kind matching.
Purpose: prevent same-name/different-type game fields from resolving to the first
field during IL copy. This is a locally modified dependency, not an upstream release.
Reproduced by scripts/patch-harmony-fieldtype.ps1. No game assembly was modified.
'@
[IO.File]::WriteAllText(
    (Join-Path $StageRuntime 'Harmony-LOCAL-MODIFICATION.txt'),
    $notice,
    (New-Object Text.UTF8Encoding($false)))

Write-Output "Staged mod: $Stage"
Write-Output "Copy the RE2Extended folder into Content\Mods."
