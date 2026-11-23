[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$BaseHarmony,
    [Parameter(Mandatory=$true)][string]$CecilPath,
    [Parameter(Mandatory=$true)][string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$BaseHarmony = [IO.Path]::GetFullPath($BaseHarmony)
$OutputPath = [IO.Path]::GetFullPath($OutputPath)

if (!(Test-Path -LiteralPath $BaseHarmony)) { throw "Base Harmony missing: $BaseHarmony" }
if (!(Test-Path -LiteralPath $CecilPath)) { throw "Mono.Cecil missing: $CecilPath" }

$null = [Reflection.Assembly]::LoadFrom($CecilPath)
$a = [Trivial.Mono.Cecil.AssemblyDefinition]::ReadAssembly($BaseHarmony)
try {
    if ($a.Name.HasPublicKey) { throw 'Unexpected signed Harmony; refusing to modify' }
    if ($a.Name.Name -ne '0Harmony') { throw "Unexpected assembly name: $($a.Name.Name)" }
    if ($a.Name.Version -lt [Version]'2.4.3.0') { throw "Expected Harmony 2.4.x; got $($a.Name.Version)" }

    $type = $a.MainModule.Types | Where-Object { $_.FullName -eq 'MonoMod.Utils.Extensions' }
    if ($type -eq $null) { throw 'MonoMod.Utils.Extensions not found' }

    $m = @($type.Methods | Where-Object {
        $_.Name -eq 'Is' -and
        $_.Parameters.Count -eq 2 -and
        $_.Parameters[0].ParameterType.FullName -eq 'Mono.Cecil.MemberReference' -and
        $_.Parameters[1].ParameterType.FullName -eq 'System.Reflection.MemberInfo'
    })
    if ($m.Count -ne 1) { throw 'Ambiguous Is(MemberReference, MemberInfo)' }
    $m = $m[0]

    # Existing field-kind branch ends before the property check. Add a field-type
    # comparison for nongeneric declaring types so same-name/different-type fields
    # are not resolved to the first field during IL copy.
    $propertyChecks = @($m.Body.Instructions | Where-Object {
        $_.OpCode.Name -eq 'isinst' -and $_.Operand.FullName -eq 'Mono.Cecil.PropertyReference'
    })
    if ($propertyChecks.Count -ne 1) { throw 'Pinned property-check shape changed' }

    $anchor = $propertyChecks[0].Previous
    if ($anchor.OpCode.Name -ne 'ldarg.0') { throw 'Pinned field matcher anchor changed' }

    $branch = $anchor.Previous.Previous.Previous
    if ($branch.OpCode.Name -ne 'beq.s' -or $branch.Operand -ne $anchor) {
        throw 'Pinned field-kind branch changed'
    }

    $fieldRef = ($m.Body.Instructions | Where-Object {
        $_.OpCode.Name -eq 'isinst' -and $_.Operand.FullName -eq 'Mono.Cecil.FieldReference'
    } | Select-Object -Last 1).Operand
    $fieldInfo = ($m.Body.Instructions | Where-Object {
        $_.OpCode.Name -eq 'isinst' -and $_.Operand.FullName -eq 'System.Reflection.FieldInfo'
    } | Select-Object -Last 1).Operand

    if ($fieldRef -eq $null -or $fieldInfo -eq $null) { throw 'Field matcher operands missing' }

    $getCecilType = New-Object Trivial.Mono.Cecil.MethodReference(
        'get_FieldType',
        $a.MainModule.GetType('Mono.Cecil.TypeReference'),
        $fieldRef)
    $getCecilType.HasThis = $true

    $getRuntimeType = $a.MainModule.ImportReference(
        [Reflection.FieldInfo].GetProperty('FieldType').GetGetMethod())

    $cecilType = $a.MainModule.GetType('Mono.Cecil.TypeReference')
    $getDeclaringType = New-Object Trivial.Mono.Cecil.MethodReference(
        'get_DeclaringType',
        $cecilType,
        $a.MainModule.GetType('Mono.Cecil.MemberReference'))
    $getDeclaringType.HasThis = $true

    $hasGenerics = New-Object Trivial.Mono.Cecil.MethodReference(
        'get_HasGenericParameters',
        $a.MainModule.TypeSystem.Boolean,
        $cecilType)
    $hasGenerics.HasThis = $true

    $isGenericInstance = New-Object Trivial.Mono.Cecil.MethodReference(
        'get_IsGenericInstance',
        $a.MainModule.TypeSystem.Boolean,
        $cecilType)
    $isGenericInstance.HasThis = $true

    $il = $m.Body.GetILProcessor()

    $first = $il.Create([Trivial.Mono.Cecil.Cil.OpCodes]::Ldarg_0)
    $pop = $il.Create([Trivial.Mono.Cecil.Cil.OpCodes]::Pop)
    $genericPop = $il.Create([Trivial.Mono.Cecil.Cil.OpCodes]::Pop)

    $insert = @(
        $first,
        $il.Create([Trivial.Mono.Cecil.Cil.OpCodes]::Isinst, $fieldRef),
        $il.Create([Trivial.Mono.Cecil.Cil.OpCodes]::Dup),
        $il.Create([Trivial.Mono.Cecil.Cil.OpCodes]::Brfalse_S, $pop),
        $il.Create([Trivial.Mono.Cecil.Cil.OpCodes]::Dup),
        $il.Create([Trivial.Mono.Cecil.Cil.OpCodes]::Callvirt, $getDeclaringType),
        $il.Create([Trivial.Mono.Cecil.Cil.OpCodes]::Callvirt, $hasGenerics),
        $il.Create([Trivial.Mono.Cecil.Cil.OpCodes]::Brtrue_S, $genericPop),
        $il.Create([Trivial.Mono.Cecil.Cil.OpCodes]::Dup),
        $il.Create([Trivial.Mono.Cecil.Cil.OpCodes]::Callvirt, $getDeclaringType),
        $il.Create([Trivial.Mono.Cecil.Cil.OpCodes]::Callvirt, $isGenericInstance),
        $il.Create([Trivial.Mono.Cecil.Cil.OpCodes]::Brtrue_S, $genericPop),
        $il.Create([Trivial.Mono.Cecil.Cil.OpCodes]::Callvirt, $getCecilType),
        $il.Create([Trivial.Mono.Cecil.Cil.OpCodes]::Ldarg_1),
        $il.Create([Trivial.Mono.Cecil.Cil.OpCodes]::Castclass, $fieldInfo),
        $il.Create([Trivial.Mono.Cecil.Cil.OpCodes]::Callvirt, $getRuntimeType),
        $il.Create([Trivial.Mono.Cecil.Cil.OpCodes]::Call, $m),
        $il.Create([Trivial.Mono.Cecil.Cil.OpCodes]::Ret),
        $genericPop,
        $il.Create([Trivial.Mono.Cecil.Cil.OpCodes]::Ldc_I4_1),
        $il.Create([Trivial.Mono.Cecil.Cil.OpCodes]::Ret),
        $pop
    )

    foreach ($i in $insert) { $il.InsertBefore($anchor, $i) }
    $branch.Operand = $first

    # Deterministic identity for the locally patched dependency.
    $a.MainModule.Mvid = [Guid]'5b8c0e3a-4f7d-4c1e-9a22-6d0b7f3b9c41'

    $dir = [IO.Path]::GetDirectoryName($OutputPath)
    if (!(Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    $a.Write($OutputPath)
}
finally {
    $a.Dispose()
}

# Clear the PE timestamp for reproducible output.
$bytes = [IO.File]::ReadAllBytes($OutputPath)
$pe = [BitConverter]::ToInt32($bytes, 0x3c)
[Array]::Clear($bytes, $pe + 8, 4)
[IO.File]::WriteAllBytes($OutputPath, $bytes)

$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $OutputPath).Hash
Write-Output "Patched Harmony: $OutputPath"
Write-Output "SHA256=$hash"
