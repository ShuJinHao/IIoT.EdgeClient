Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-DevelopmentLifecycleAstGuard {
    param(
        [Parameter(Mandatory)][string]$DevelopmentSource,
        [switch]$SkipSourceDigest,
        [switch]$SkipFunctionDigest,
        [switch]$SkipOuterFlowDigest
    )

    $tokens = $null
    $parseErrors = $null
    $developmentAst = [System.Management.Automation.Language.Parser]::ParseInput(
        $DevelopmentSource, [ref]$tokens, [ref]$parseErrors)
    if (@($parseErrors).Count -ne 0) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 development validation source has PowerShell parse errors.'
    }
    $developmentSourceBytes = [Text.UTF8Encoding]::new($false).GetBytes($DevelopmentSource)
    $developmentSourceDigest = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData($developmentSourceBytes)).ToLowerInvariant()
    if (-not $SkipSourceDigest -and
        ($developmentSourceBytes.Length -ne 66772 -or
        $developmentSourceDigest -cne '16f66703fc7edd2821899da5fbb3a0d5be64d8b6d1faf6221739a0895ffe6bde')) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 development canonical source bytes changed.'
    }
    if ($null -eq $developmentAst.ParamBlock) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 development parameter contract is missing before TMPDIR pin.'
    }
    $developmentScriptAttributes = @($developmentAst.ParamBlock.Attributes)
    if ($developmentScriptAttributes.Count -ne 1 -or
        $developmentScriptAttributes[0] -isnot
            [System.Management.Automation.Language.AttributeAst] -or
        -not [string]::Equals(
            [string]$developmentScriptAttributes[0].TypeName.FullName,
            'CmdletBinding', [StringComparison]::OrdinalIgnoreCase) -or
        @($developmentScriptAttributes[0].PositionalArguments).Count -ne 0 -or
        @($developmentScriptAttributes[0].NamedArguments).Count -ne 0 -or
        [string]$developmentScriptAttributes[0].Extent.Text -cne '[CmdletBinding()]') {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 development CmdletBinding contract changed before TMPDIR pin.'
    }
    $developmentParamBytes = [Text.UTF8Encoding]::new($false).GetBytes(
        [string]$developmentAst.ParamBlock.Extent.Text)
    $developmentParamDigest = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData(
            $developmentParamBytes)).ToLowerInvariant()
    if (@($developmentAst.ParamBlock.Parameters).Count -ne 6 -or
        $developmentParamBytes.Length -ne 564 -or
        $developmentParamDigest -cne
            '735c1a22572726f43955638c698e149e79d5d99fda5f594c5b8f0c18c4a869d0') {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 development parameter contract changed before TMPDIR pin.'
    }

    $isWithin = {
        param([object]$Node, [object]$Ancestor)
        $current = $Node
        while ($null -ne $current) {
            if ([object]::ReferenceEquals($current, $Ancestor)) { return $true }
            $current = $current.Parent
        }
        return $false
    }
    $isVariable = {
        param([object]$Node, [string]$Name)
        return $Node -is [System.Management.Automation.Language.VariableExpressionAst] -and
            [string]::Equals(
                [string]$Node.VariablePath.UserPath, $Name,
                [StringComparison]::OrdinalIgnoreCase)
    }
    $isStaticMember = {
        param([object]$Node, [string]$TypeName, [string]$MemberName)
        return $Node -is [System.Management.Automation.Language.MemberExpressionAst] -and
            $Node -isnot [System.Management.Automation.Language.InvokeMemberExpressionAst] -and
            $Node.Static -and
            $Node.Expression -is [System.Management.Automation.Language.TypeExpressionAst] -and
            [string]::Equals(
                [string]$Node.Expression.TypeName.FullName, $TypeName,
                [StringComparison]::OrdinalIgnoreCase) -and
            $Node.Member -is [System.Management.Automation.Language.StringConstantExpressionAst] -and
            [string]::Equals(
                [string]$Node.Member.Value, $MemberName,
                [StringComparison]::OrdinalIgnoreCase)
    }
    $isInstanceMember = {
        param([object]$Node, [string]$VariableName, [string]$MemberName)
        return $Node -is [System.Management.Automation.Language.MemberExpressionAst] -and
            $Node -isnot [System.Management.Automation.Language.InvokeMemberExpressionAst] -and
            -not $Node.Static -and
            (& $isVariable $Node.Expression $VariableName) -and
            $Node.Member -is [System.Management.Automation.Language.StringConstantExpressionAst] -and
            [string]::Equals(
                [string]$Node.Member.Value, $MemberName,
                [StringComparison]::OrdinalIgnoreCase)
    }
    $isStaticInvocation = {
        param([object]$Node, [string]$TypeName, [string]$MemberName)
        return $Node -is [System.Management.Automation.Language.InvokeMemberExpressionAst] -and
            $Node.Static -and
            $Node.Expression -is [System.Management.Automation.Language.TypeExpressionAst] -and
            [string]::Equals(
                [string]$Node.Expression.TypeName.FullName, $TypeName,
                [StringComparison]::OrdinalIgnoreCase) -and
            $Node.Member -is [System.Management.Automation.Language.StringConstantExpressionAst] -and
            [string]::Equals(
                [string]$Node.Member.Value, $MemberName,
                [StringComparison]::OrdinalIgnoreCase)
    }
    $isProcessTarget = {
        param([object]$Node)
        return (& $isStaticMember $Node 'EnvironmentVariableTarget' 'Process')
    }
    $isConstantString = {
        param([object]$Node, [string]$Value)
        return $Node -is [System.Management.Automation.Language.StringConstantExpressionAst] -and
            [string]::Equals([string]$Node.Value, $Value, [StringComparison]::Ordinal)
    }
    $isStringCastOfVariable = {
        param([object]$Node, [string]$VariableName)
        return $Node -is [System.Management.Automation.Language.ConvertExpressionAst] -and
            [string]::Equals(
                [string]$Node.Type.TypeName.FullName, 'string',
                [StringComparison]::OrdinalIgnoreCase) -and
            (& $isVariable $Node.Child $VariableName)
    }
    $matchesVariableName = {
        param([object]$Node, [string]$Name)
        if ($Node -isnot [System.Management.Automation.Language.VariableExpressionAst]) {
            return $false
        }
        $userPath = [string]$Node.VariablePath.UserPath
        if ([string]::Equals(
                $userPath, $Name, [StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
        foreach ($scopeName in @('script', 'local', 'global', 'private', 'variable')) {
            if ([string]::Equals(
                    $userPath, "${scopeName}:$Name",
                    [StringComparison]::OrdinalIgnoreCase)) {
                return $true
            }
        }
        return $false
    }
    $getVariableAssignments = {
        param([object]$Scope, [string]$Name)
        $matchingAssignments = @($Scope.FindAll({
                    param($node)
                    $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
                    $node.Left -is [System.Management.Automation.Language.VariableExpressionAst]
                }, $true) | Where-Object { & $matchesVariableName $_.Left $Name })
        if (@($matchingAssignments | Where-Object {
                    -not (& $isVariable $_.Left $Name)
                }).Count -ne 0) {
            throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 protected dataflow may not use a scoped variable alias.'
        }
        $referenceWrites = @($Scope.FindAll({
                    param($node)
                    $node -is [System.Management.Automation.Language.ConvertExpressionAst] -and
                    [string]::Equals(
                        [string]$node.Type.TypeName.FullName, 'ref',
                        [StringComparison]::OrdinalIgnoreCase) -and
                    $node.Child -is [System.Management.Automation.Language.VariableExpressionAst]
                }, $true) | Where-Object { & $matchesVariableName $_.Child $Name })
        if ($referenceWrites.Count -ne 0) {
            throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 protected dataflow may not escape through a reference.'
        }
        return $matchingAssignments
    }
    $testDirectFailureCatch = {
        param([object]$CatchAst, [string]$VariableName)
        if ($CatchAst -isnot [System.Management.Automation.Language.CatchClauseAst] -or
            @($CatchAst.CatchTypes).Count -ne 0 -or
            @($CatchAst.Body.Statements).Count -ne 1 -or
            $null -ne $CatchAst.Body.Traps) {
            return $false
        }
        $assignment = $CatchAst.Body.Statements[0]
        return $assignment -is [System.Management.Automation.Language.AssignmentStatementAst] -and
            $assignment.Operator -eq [System.Management.Automation.Language.TokenKind]::Equals -and
            (& $isVariable $assignment.Left $VariableName) -and
            $assignment.Right -is [System.Management.Automation.Language.CommandExpressionAst] -and
            (& $isVariable $assignment.Right.Expression '_')
    }
    $testGuardedFailureCatch = {
        param([object]$CatchAst, [string]$VariableName)
        if ($CatchAst -isnot [System.Management.Automation.Language.CatchClauseAst] -or
            @($CatchAst.CatchTypes).Count -ne 0 -or
            @($CatchAst.Body.Statements).Count -ne 1 -or
            $null -ne $CatchAst.Body.Traps) {
            return $false
        }
        $ifStatement = $CatchAst.Body.Statements[0]
        if ($ifStatement -isnot [System.Management.Automation.Language.IfStatementAst] -or
            @($ifStatement.Clauses).Count -ne 1 -or $null -ne $ifStatement.ElseClause) {
            return $false
        }
        $condition = $ifStatement.Clauses[0].Item1
        if ($condition -isnot [System.Management.Automation.Language.PipelineAst] -or
            @($condition.PipelineElements).Count -ne 1 -or
            $condition.PipelineElements[0] -isnot [System.Management.Automation.Language.CommandExpressionAst] -or
            $condition.PipelineElements[0].Expression -isnot [System.Management.Automation.Language.BinaryExpressionAst]) {
            return $false
        }
        $binary = $condition.PipelineElements[0].Expression
        if ($binary.Operator -ne [System.Management.Automation.Language.TokenKind]::Ieq -or
            -not (& $isVariable $binary.Left 'null') -or
            -not (& $isVariable $binary.Right $VariableName)) {
            return $false
        }
        $body = $ifStatement.Clauses[0].Item2
        if (@($body.Statements).Count -ne 1 -or $null -ne $body.Traps) { return $false }
        $assignment = $body.Statements[0]
        return $assignment -is [System.Management.Automation.Language.AssignmentStatementAst] -and
            $assignment.Operator -eq [System.Management.Automation.Language.TokenKind]::Equals -and
            (& $isVariable $assignment.Left $VariableName) -and
            $assignment.Right -is [System.Management.Automation.Language.CommandExpressionAst] -and
            (& $isVariable $assignment.Right.Expression '_')
    }
    $testSingleVariableCondition = {
        param([object]$Condition, [string]$VariableName)
        return $Condition -is [System.Management.Automation.Language.PipelineAst] -and
            @($Condition.PipelineElements).Count -eq 1 -and
            $Condition.PipelineElements[0] -is [System.Management.Automation.Language.CommandExpressionAst] -and
            (& $isVariable $Condition.PipelineElements[0].Expression $VariableName)
    }
    $testProcessEnvironmentAssignment = {
        param([object]$Assignment)
        if ($Assignment -isnot [System.Management.Automation.Language.AssignmentStatementAst] -or
            $Assignment.Right -isnot [System.Management.Automation.Language.CommandExpressionAst]) {
            return $false
        }
        $invocation = $Assignment.Right.Expression
        return (& $isStaticInvocation $invocation 'Environment' 'GetEnvironmentVariables') -and
            @($invocation.Arguments).Count -eq 1 -and (& $isProcessTarget $invocation.Arguments[0])
    }
    $testCanonicalFileRemoval = {
        param([object]$Command, [string]$VariableName)
        $elements = @($Command.CommandElements)
        return $Command -is [System.Management.Automation.Language.CommandAst] -and
            [string]::Equals(
                [string]$Command.GetCommandName(), 'Remove-Item',
                [StringComparison]::OrdinalIgnoreCase) -and
            $elements.Count -eq 4 -and
            $elements[1] -is [System.Management.Automation.Language.CommandParameterAst] -and
            [string]::Equals(
                [string]$elements[1].ParameterName, 'LiteralPath',
                [StringComparison]::OrdinalIgnoreCase) -and
            (& $isVariable $elements[2] $VariableName) -and
            $elements[3] -is [System.Management.Automation.Language.CommandParameterAst] -and
            [string]::Equals(
                [string]$elements[3].ParameterName, 'Force',
                [StringComparison]::OrdinalIgnoreCase)
    }
    $testCanonicalDirectoryCreation = {
        param([object]$Command, [string]$VariableName, [bool]$SplitParent)
        if ($Command -isnot [System.Management.Automation.Language.CommandAst] -or
            -not [string]::Equals(
                [string]$Command.GetCommandName(), 'New-Item',
                [StringComparison]::OrdinalIgnoreCase)) {
            return $false
        }
        $elements = @($Command.CommandElements)
        $expectedCount = if ($SplitParent) { 6 } else { 5 }
        if ($elements.Count -ne $expectedCount -or
            $elements[1] -isnot [System.Management.Automation.Language.CommandParameterAst] -or
            -not [string]::Equals(
                [string]$elements[1].ParameterName, 'ItemType',
                [StringComparison]::OrdinalIgnoreCase) -or
            -not (& $isConstantString $elements[2] 'Directory') -or
            $elements[3] -isnot [System.Management.Automation.Language.CommandParameterAst] -or
            -not [string]::Equals(
                [string]$elements[3].ParameterName, 'Path',
                [StringComparison]::OrdinalIgnoreCase)) {
            return $false
        }
        if (-not $SplitParent) {
            return (& $isVariable $elements[4] $VariableName)
        }
        if ($elements[4] -isnot [System.Management.Automation.Language.ParenExpressionAst] -or
            $elements[4].Pipeline -isnot [System.Management.Automation.Language.PipelineAst] -or
            @($elements[4].Pipeline.PipelineElements).Count -ne 1 -or
            $elements[4].Pipeline.PipelineElements[0] -isnot
                [System.Management.Automation.Language.CommandAst]) {
            return $false
        }
        $splitCommand = $elements[4].Pipeline.PipelineElements[0]
        $splitElements = @($splitCommand.CommandElements)
        return [string]::Equals(
                [string]$splitCommand.GetCommandName(), 'Split-Path',
                [StringComparison]::OrdinalIgnoreCase) -and
            $splitElements.Count -eq 3 -and
            (& $isVariable $splitElements[1] $VariableName) -and
            $splitElements[2] -is [System.Management.Automation.Language.CommandParameterAst] -and
            [string]::Equals(
                [string]$splitElements[2].ParameterName, 'Parent',
                [StringComparison]::OrdinalIgnoreCase) -and
            $elements[5] -is [System.Management.Automation.Language.CommandParameterAst] -and
            [string]::Equals(
                [string]$elements[5].ParameterName, 'Force',
                [StringComparison]::OrdinalIgnoreCase)
    }

    $forbiddenPhysicalPrefix = '/' + 'private'
    $executableStrings = @($developmentAst.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.StringConstantExpressionAst] -or
                $node -is [System.Management.Automation.Language.ExpandableStringExpressionAst]
            }, $true))
    foreach ($stringAst in $executableStrings) {
        if ([string]$stringAst.Value -and
            ([string]$stringAst.Value).IndexOf(
                $forbiddenPhysicalPrefix, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 development validation contains an executable hard-coded physical prefix.'
        }
    }

    $expectedDevelopmentFunctionNames = @(
        'Sort-DevOrdinalStrings',
        'ConvertFrom-DevUtf8',
        'Invoke-DevProcess',
        'Invoke-DevGitBytes',
        'Invoke-DevGitText',
        'Get-DevLocalGitConfigDigest',
        'Resolve-DevPhysicalTempRoot',
        'Assert-DevRepositoryRoot',
        'Assert-DevSafePath',
        'Get-DevCurrentFileMode',
        'Get-DevDirtyManifest',
        'Update-DevSnapshotIndexFromManifest',
        'New-DevIndependentSnapshot')
    $developmentFunctionDefinitions = @($developmentAst.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.FunctionDefinitionAst]
            }, $true))
    if ($developmentFunctionDefinitions.Count -ne
        $expectedDevelopmentFunctionNames.Count) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 development function inventory count changed.'
    }
    foreach ($functionName in $expectedDevelopmentFunctionNames) {
        $functionOwners = @($developmentFunctionDefinitions | Where-Object {
                [string]::Equals(
                    [string]$_.Name, $functionName,
                    [StringComparison]::OrdinalIgnoreCase)
            })
        if ($functionOwners.Count -ne 1 -or
            -not [object]::ReferenceEquals(
                $functionOwners[0].Parent, $developmentAst.EndBlock) -or
            $functionOwners[0].IsFilter -or $functionOwners[0].IsWorkflow) {
            throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 development functions must match the exact unique top-level inventory.'
        }
    }
    $developmentFunctionRows = @($developmentFunctionDefinitions |
        ForEach-Object {
            [string]$_.Name + '|' + [string]$_.Extent.Text
        })
    $developmentFunctionText = $developmentFunctionRows -join "`n"
    $developmentFunctionBytes = [Text.UTF8Encoding]::new($false).GetBytes(
        $developmentFunctionText)
    $developmentFunctionDigest = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData(
            $developmentFunctionBytes)).ToLowerInvariant()
    if (-not $SkipFunctionDigest -and
        ($developmentFunctionRows.Count -ne 13 -or
        $developmentFunctionBytes.Length -ne 41051 -or
        $developmentFunctionDigest -cne
            'dbdeae539a568761260c84684cd32be1c0c95ddb23d902219789d079d526b400')) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 development canonical function bytes changed.'
    }

    $dirtyManifestFunction = @($developmentFunctionDefinitions | Where-Object {
            [string]::Equals(
                [string]$_.Name, 'Get-DevDirtyManifest',
                [StringComparison]::OrdinalIgnoreCase)
        })[0]
    $manifestByteAssignments = @($dirtyManifestFunction.Body.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.AssignmentStatementAst]
            }, $true) | Where-Object {
            $left = $_.Left
            if ($left -is [System.Management.Automation.Language.ConvertExpressionAst]) {
                $left = $left.Child
            }
            & $isVariable $left 'manifestBytes'
        })
    if ($manifestByteAssignments.Count -ne 1) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 development dirty manifest canonical byte-array shape changed.'
    }
    $manifestByteAssignment = $manifestByteAssignments[0]
    $manifestByteLeft = $manifestByteAssignment.Left
    $manifestByteRight = $manifestByteAssignment.Right
    if ($manifestByteAssignment.Operator -ne
            [System.Management.Automation.Language.TokenKind]::Equals -or
        $manifestByteLeft -isnot
            [System.Management.Automation.Language.ConvertExpressionAst] -or
        -not [string]::Equals(
            [string]$manifestByteLeft.Type.TypeName.FullName, 'byte[]',
            [StringComparison]::OrdinalIgnoreCase) -or
        -not (& $isVariable $manifestByteLeft.Child 'manifestBytes') -or
        $manifestByteRight -isnot
            [System.Management.Automation.Language.CommandExpressionAst] -or
        $manifestByteRight.Expression -isnot
            [System.Management.Automation.Language.ArrayExpressionAst]) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 development dirty manifest canonical byte-array shape changed.'
    }
    $manifestByteStatements = @(
        $manifestByteRight.Expression.SubExpression.Statements)
    if ($manifestByteStatements.Count -ne 1 -or
        $manifestByteStatements[0] -isnot
            [System.Management.Automation.Language.PipelineAst] -or
        @($manifestByteStatements[0].PipelineElements).Count -ne 1 -or
        $manifestByteStatements[0].PipelineElements[0] -isnot
            [System.Management.Automation.Language.CommandAst]) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 development dirty manifest canonical byte-array shape changed.'
    }
    $manifestByteCommand = $manifestByteStatements[0].PipelineElements[0]
    if (-not [string]::Equals(
            [string]$manifestByteCommand.GetCommandName(),
            'ConvertTo-EdgeCanonicalBytes',
            [StringComparison]::OrdinalIgnoreCase) -or
        @($manifestByteCommand.CommandElements).Count -ne 2 -or
        -not (& $isVariable $manifestByteCommand.CommandElements[1] 'manifest')) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 development dirty manifest canonical byte-array shape changed.'
    }

    $invokeDevProcessDefinitions = @($developmentAst.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
                [string]::Equals(
                    [string]$node.Name, 'Invoke-DevProcess',
                    [StringComparison]::OrdinalIgnoreCase)
            }, $true))
    if ($invokeDevProcessDefinitions.Count -ne 1 -or
        -not [object]::ReferenceEquals(
            $invokeDevProcessDefinitions[0].Parent, $developmentAst.EndBlock)) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 development validation must define exactly one top-level Invoke-DevProcess owner.'
    }
    $invokeDevProcess = $invokeDevProcessDefinitions[0]
    $invokeDevProcessCalls = @($developmentAst.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.CommandAst] -and
                [string]::Equals(
                    [string]$node.GetCommandName(), 'Invoke-DevProcess',
                    [StringComparison]::OrdinalIgnoreCase)
            }, $true))
    $invokeDevGitBytesDefinitions = @($developmentAst.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
                [string]::Equals(
                    [string]$node.Name, 'Invoke-DevGitBytes',
                    [StringComparison]::OrdinalIgnoreCase)
            }, $true))
    $deferredGitProcessCalls = @(if ($invokeDevGitBytesDefinitions.Count -eq 1) {
        $invokeDevProcessCalls | Where-Object {
                & $isWithin $_ $invokeDevGitBytesDefinitions[0].Body
            }
    }
    else { @() })
    if ($invokeDevProcessCalls.Count -ne 5 -or
        $invokeDevGitBytesDefinitions.Count -ne 1 -or
        -not [object]::ReferenceEquals(
            $invokeDevGitBytesDefinitions[0].Parent, $developmentAst.EndBlock) -or
        $deferredGitProcessCalls.Count -ne 1) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 Invoke-DevProcess call ownership/count changed.'
    }

    $invokeDevGitByteCalls = @($developmentAst.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.CommandAst] -and
                [string]::Equals(
                    [string]$node.GetCommandName(), 'Invoke-DevGitBytes',
                    [StringComparison]::OrdinalIgnoreCase)
            }, $true))
    $voidGitCalls = [Collections.Generic.List[object]]::new()
    $checkoutGitCalls = [Collections.Generic.List[object]]::new()
    foreach ($gitCall in $invokeDevGitByteCalls) {
        $literalArguments = @($gitCall.FindAll({
                    param($node)
                    $node -is [System.Management.Automation.Language.StringConstantExpressionAst]
                }, $true) | ForEach-Object { [string]$_.Value })
        if (@($literalArguments | Where-Object {
                    $_ -in @('stash', 'reset', 'clean')
                }).Count -ne 0 -or
            ($literalArguments -contains 'add' -and $literalArguments -contains '-A')) {
            throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 development Git call contains a forbidden broad index/worktree command.'
        }
        if ($literalArguments -contains 'checkout') {
            $checkoutGitCalls.Add($gitCall)
        }
        $ancestor = $gitCall.Parent
        while ($null -ne $ancestor -and
            $ancestor -isnot [System.Management.Automation.Language.ConvertExpressionAst] -and
            $ancestor -isnot [System.Management.Automation.Language.FunctionDefinitionAst]) {
            $ancestor = $ancestor.Parent
        }
        if ($ancestor -is [System.Management.Automation.Language.ConvertExpressionAst] -and
            [string]::Equals(
                [string]$ancestor.Type.TypeName.FullName, 'void',
                [StringComparison]::OrdinalIgnoreCase)) {
            $voidGitCalls.Add($gitCall)
        }
    }
    if ($checkoutGitCalls.Count -ne 1 -or
        -not [Text.RegularExpressions.Regex]::IsMatch(
            [string]$checkoutGitCalls[0].Extent.Text,
            "@\('checkout',\s*'--detach',\s*\[string\]\`$Manifest\.value\.sourceBaseHead\)$",
            [Text.RegularExpressions.RegexOptions]::CultureInvariant)) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 development Git checkout is not the one exact detached manifest HEAD checkout.'
    }
    $voidGitKinds = @($voidGitCalls | ForEach-Object {
            $text = [string]$_.Extent.Text
            if ($text.Contains("@('clone'", [StringComparison]::Ordinal)) { 'clone' }
            elseif ($text.Contains("@('checkout'", [StringComparison]::Ordinal)) { 'checkout' }
            elseif ($text.Contains("'update-index'", [StringComparison]::Ordinal)) { 'update-index' }
            elseif ($text.Contains("'commit'", [StringComparison]::Ordinal)) { 'commit' }
            else { 'unknown' }
        })
    [Array]::Sort($voidGitKinds, [StringComparer]::Ordinal)
    if ($voidGitCalls.Count -ne 4 -or
        ($voidGitKinds -join '|') -cne 'checkout|clone|commit|update-index') {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 development void Git consumers must be exactly clone/checkout/update-index/commit.'
    }

    $startInfoAssignments = @(& $getVariableAssignments $developmentAst 'startInfo')
    $processStartInfoInvocations = @($developmentAst.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.InvokeMemberExpressionAst] -and
                $node.Static -and
                $node.Expression -is [System.Management.Automation.Language.TypeExpressionAst] -and
                ([string]::Equals(
                        [string]$node.Expression.TypeName.FullName,
                        'Diagnostics.ProcessStartInfo',
                        [StringComparison]::OrdinalIgnoreCase) -or
                    [string]::Equals(
                        [string]$node.Expression.TypeName.FullName,
                        'System.Diagnostics.ProcessStartInfo',
                        [StringComparison]::OrdinalIgnoreCase))
            }, $true))
    $processStartInfoTypeExpressions = @($developmentAst.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.TypeExpressionAst] -and
                ([string]::Equals(
                        [string]$node.TypeName.FullName, 'Diagnostics.ProcessStartInfo',
                        [StringComparison]::OrdinalIgnoreCase) -or
                    [string]::Equals(
                        [string]$node.TypeName.FullName, 'System.Diagnostics.ProcessStartInfo',
                        [StringComparison]::OrdinalIgnoreCase))
            }, $true))
    $startInfoCreation = if (
        $startInfoAssignments.Count -eq 1 -and
        $startInfoAssignments[0].Right -is
            [System.Management.Automation.Language.CommandExpressionAst]) {
        $startInfoAssignments[0].Right.Expression
    }
    else { $null }
    if ($startInfoAssignments.Count -ne 1 -or
        $startInfoAssignments[0].Operator -ne
            [System.Management.Automation.Language.TokenKind]::Equals -or
        -not [object]::ReferenceEquals(
            $startInfoAssignments[0].Parent, $invokeDevProcess.Body.EndBlock) -or
        $processStartInfoInvocations.Count -ne 1 -or
        $processStartInfoTypeExpressions.Count -ne 1 -or
        -not [object]::ReferenceEquals(
            $processStartInfoTypeExpressions[0], $startInfoCreation.Expression) -or
        -not [object]::ReferenceEquals(
            $processStartInfoInvocations[0], $startInfoCreation) -or
        -not (& $isStaticInvocation `
            $startInfoCreation 'Diagnostics.ProcessStartInfo' 'new') -or
        $null -ne $startInfoCreation.Arguments) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 Invoke-DevProcess must create exactly one direct ProcessStartInfo owner.'
    }

    $processAssignments = @(& $getVariableAssignments $developmentAst 'process')
    $processTypeInvocations = @($developmentAst.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.InvokeMemberExpressionAst] -and
                $node.Static -and
                $node.Expression -is [System.Management.Automation.Language.TypeExpressionAst] -and
                ([string]::Equals(
                        [string]$node.Expression.TypeName.FullName, 'Diagnostics.Process',
                        [StringComparison]::OrdinalIgnoreCase) -or
                    [string]::Equals(
                        [string]$node.Expression.TypeName.FullName, 'System.Diagnostics.Process',
                        [StringComparison]::OrdinalIgnoreCase))
            }, $true))
    $processTypeExpressions = @($developmentAst.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.TypeExpressionAst] -and
                ([string]::Equals(
                        [string]$node.TypeName.FullName, 'Diagnostics.Process',
                        [StringComparison]::OrdinalIgnoreCase) -or
                    [string]::Equals(
                        [string]$node.TypeName.FullName, 'System.Diagnostics.Process',
                        [StringComparison]::OrdinalIgnoreCase))
            }, $true))
    $processCreation = if (
        $processAssignments.Count -eq 1 -and
        $processAssignments[0].Right -is
            [System.Management.Automation.Language.CommandExpressionAst]) {
        $processAssignments[0].Right.Expression
    }
    else { $null }
    $processStartInfoBindings = @($developmentAst.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
                $node.Left -is [System.Management.Automation.Language.MemberExpressionAst] -and
                $node.Left -isnot [System.Management.Automation.Language.InvokeMemberExpressionAst] -and
                $node.Left.Member -is
                    [System.Management.Automation.Language.StringConstantExpressionAst] -and
                [string]::Equals(
                    [string]$node.Left.Member.Value, 'StartInfo',
                    [StringComparison]::OrdinalIgnoreCase)
            }, $true))
    $processStartInfoMembers = @($developmentAst.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.MemberExpressionAst] -and
                $node.Member -is
                    [System.Management.Automation.Language.StringConstantExpressionAst] -and
                [string]::Equals(
                    [string]$node.Member.Value, 'StartInfo',
                    [StringComparison]::OrdinalIgnoreCase)
            }, $true))
    $processStartInvocations = @($developmentAst.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.InvokeMemberExpressionAst] -and
                $node.Member -is
                    [System.Management.Automation.Language.StringConstantExpressionAst] -and
                [string]::Equals(
                    [string]$node.Member.Value, 'Start',
                    [StringComparison]::OrdinalIgnoreCase)
            }, $true))
    $processOwnerTries = @($invokeDevProcess.Body.EndBlock.Statements | Where-Object {
            $_ -is [System.Management.Automation.Language.TryStatementAst] -and
            $null -ne $_.Finally
        })
    if ($processAssignments.Count -ne 1 -or
        $processAssignments[0].Operator -ne
            [System.Management.Automation.Language.TokenKind]::Equals -or
        -not [object]::ReferenceEquals(
            $processAssignments[0].Parent, $invokeDevProcess.Body.EndBlock) -or
        $processTypeInvocations.Count -ne 1 -or
        $processTypeExpressions.Count -ne 1 -or
        -not [object]::ReferenceEquals($processTypeInvocations[0], $processCreation) -or
        -not [object]::ReferenceEquals(
            $processTypeExpressions[0], $processCreation.Expression) -or
        -not (& $isStaticInvocation $processCreation 'Diagnostics.Process' 'new') -or
        $null -ne $processCreation.Arguments -or
        $processStartInfoBindings.Count -ne 1 -or
        $processStartInfoMembers.Count -ne 1 -or
        -not [object]::ReferenceEquals(
            $processStartInfoMembers[0], $processStartInfoBindings[0].Left) -or
        -not (& $isInstanceMember $processStartInfoBindings[0].Left 'process' 'StartInfo') -or
        $processStartInfoBindings[0].Right -isnot
            [System.Management.Automation.Language.CommandExpressionAst] -or
        -not (& $isVariable $processStartInfoBindings[0].Right.Expression 'startInfo') -or
        -not [object]::ReferenceEquals(
            $processStartInfoBindings[0].Parent, $invokeDevProcess.Body.EndBlock) -or
        $processStartInvocations.Count -ne 1 -or
        $processStartInvocations[0].Static -or
        -not (& $isVariable $processStartInvocations[0].Expression 'process') -or
        $null -ne $processStartInvocations[0].Arguments -or
        $processOwnerTries.Count -ne 1 -or
        -not (& $isWithin $processStartInvocations[0] $processOwnerTries[0].Body)) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 Invoke-DevProcess Process owner/start binding inventory changed.'
    }

    $allowedStartInfoMembers = [ordered]@{
        'FileName' = 1
        'WorkingDirectory' = 1
        'UseShellExecute' = 1
        'CreateNoWindow' = 1
        'RedirectStandardOutput' = 1
        'RedirectStandardError' = 1
        'RedirectStandardInput' = 1
        'ArgumentList' = 1
        'Environment' = 1
    }
    $actualStartInfoMembers = @{}
    $startInfoMembers = @($invokeDevProcess.Body.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.MemberExpressionAst] -and
                $node.Expression -is [System.Management.Automation.Language.VariableExpressionAst] -and
                [string]::Equals(
                    [string]$node.Expression.VariablePath.UserPath, 'startInfo',
                    [StringComparison]::OrdinalIgnoreCase)
            }, $true))
    foreach ($member in $startInfoMembers) {
        if ($member -is [System.Management.Automation.Language.InvokeMemberExpressionAst] -or
            $member.Static -or
            $member.Member -isnot
                [System.Management.Automation.Language.StringConstantExpressionAst] -or
            -not $allowedStartInfoMembers.Contains([string]$member.Member.Value)) {
            throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 Invoke-DevProcess contains a dynamic or forbidden ProcessStartInfo member access.'
        }
        $memberName = [string]$member.Member.Value
        $actualStartInfoMembers[$memberName] = 1 + [int]$actualStartInfoMembers[$memberName]
    }
    foreach ($memberName in $allowedStartInfoMembers.Keys) {
        if ([int]$actualStartInfoMembers[$memberName] -ne
            [int]$allowedStartInfoMembers[$memberName]) {
            throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 Invoke-DevProcess ProcessStartInfo member inventory changed.'
        }
    }

    $startInfoPropertyAssignments = @($invokeDevProcess.Body.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
                $node.Left -is [System.Management.Automation.Language.MemberExpressionAst] -and
                $node.Left -isnot [System.Management.Automation.Language.InvokeMemberExpressionAst] -and
                $node.Left.Expression -is
                    [System.Management.Automation.Language.VariableExpressionAst] -and
                [string]::Equals(
                    [string]$node.Left.Expression.VariablePath.UserPath, 'startInfo',
                    [StringComparison]::OrdinalIgnoreCase)
            }, $true))
    $expectedStartInfoProperties = @(
        'FileName', 'WorkingDirectory', 'UseShellExecute', 'CreateNoWindow',
        'RedirectStandardOutput', 'RedirectStandardError', 'RedirectStandardInput')
    if ($startInfoPropertyAssignments.Count -ne $expectedStartInfoProperties.Count) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 Invoke-DevProcess ProcessStartInfo property assignment count changed.'
    }
    foreach ($propertyName in $expectedStartInfoProperties) {
        $matches = @($startInfoPropertyAssignments | Where-Object {
                $_.Operator -eq [System.Management.Automation.Language.TokenKind]::Equals -and
                $_.Left.Member -is
                    [System.Management.Automation.Language.StringConstantExpressionAst] -and
                [string]::Equals(
                    [string]$_.Left.Member.Value, $propertyName,
                    [StringComparison]::OrdinalIgnoreCase) -and
                [object]::ReferenceEquals($_.Parent, $invokeDevProcess.Body.EndBlock)
            })
        if ($matches.Count -ne 1) {
            throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 Invoke-DevProcess ProcessStartInfo property dataflow changed.'
        }
    }
    $directStartInfoIndexWrites = @($invokeDevProcess.Body.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
                $node.Left -is [System.Management.Automation.Language.IndexExpressionAst] -and
                $node.Left.Target -is
                    [System.Management.Automation.Language.VariableExpressionAst] -and
                [string]::Equals(
                    [string]$node.Left.Target.VariablePath.UserPath, 'startInfo',
                    [StringComparison]::OrdinalIgnoreCase)
            }, $true))
    if ($directStartInfoIndexWrites.Count -ne 0) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 Invoke-DevProcess may not replace ProcessStartInfo members through an index.'
    }

    $environmentOverlayIfs = @($invokeDevProcess.Body.EndBlock.Statements | Where-Object {
            if ($_ -isnot [System.Management.Automation.Language.IfStatementAst] -or
                @($_.Clauses).Count -ne 1 -or $null -ne $_.ElseClause) {
                return $false
            }
            $condition = $_.Clauses[0].Item1
            if ($condition -isnot [System.Management.Automation.Language.PipelineAst] -or
                @($condition.PipelineElements).Count -ne 1 -or
                $condition.PipelineElements[0] -isnot
                    [System.Management.Automation.Language.CommandExpressionAst] -or
                $condition.PipelineElements[0].Expression -isnot
                    [System.Management.Automation.Language.BinaryExpressionAst]) {
                return $false
            }
            $binary = $condition.PipelineElements[0].Expression
            return $binary.Operator -eq
                [System.Management.Automation.Language.TokenKind]::Ine -and
                (& $isVariable $binary.Left 'null') -and
                (& $isVariable $binary.Right 'Environment')
        })
    if ($environmentOverlayIfs.Count -ne 1 -or
        @($environmentOverlayIfs[0].Clauses[0].Item2.Statements).Count -ne 1 -or
        $null -ne $environmentOverlayIfs[0].Clauses[0].Item2.Traps -or
        $environmentOverlayIfs[0].Clauses[0].Item2.Statements[0] -isnot
            [System.Management.Automation.Language.ForEachStatementAst]) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 Invoke-DevProcess environment overlay gate changed.'
    }
    $environmentForEach = $environmentOverlayIfs[0].Clauses[0].Item2.Statements[0]
    if (-not (& $isVariable $environmentForEach.Variable 'name') -or
        $environmentForEach.Condition -isnot
            [System.Management.Automation.Language.PipelineAst] -or
        @($environmentForEach.Condition.PipelineElements).Count -ne 1 -or
        $environmentForEach.Condition.PipelineElements[0] -isnot
            [System.Management.Automation.Language.CommandExpressionAst] -or
        -not (& $isInstanceMember `
            $environmentForEach.Condition.PipelineElements[0].Expression `
            'Environment' 'Keys') -or
        @($environmentForEach.Body.Statements).Count -ne 2 -or
        $null -ne $environmentForEach.Body.Traps -or
        $environmentForEach.Body.Statements[0] -isnot
            [System.Management.Automation.Language.IfStatementAst] -or
        $environmentForEach.Body.Statements[1] -isnot
            [System.Management.Automation.Language.AssignmentStatementAst]) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 Invoke-DevProcess environment overlay iteration changed.'
    }

    $tmpdirGuard = $environmentForEach.Body.Statements[0]
    $tmpdirGuardCondition = if (
        @($tmpdirGuard.Clauses).Count -eq 1 -and $null -eq $tmpdirGuard.ElseClause -and
        $tmpdirGuard.Clauses[0].Item1 -is
            [System.Management.Automation.Language.PipelineAst] -and
        @($tmpdirGuard.Clauses[0].Item1.PipelineElements).Count -eq 1 -and
        $tmpdirGuard.Clauses[0].Item1.PipelineElements[0] -is
            [System.Management.Automation.Language.CommandExpressionAst]) {
        $tmpdirGuard.Clauses[0].Item1.PipelineElements[0].Expression
    }
    else { $null }
    $tmpdirGuardBody = if (@($tmpdirGuard.Clauses).Count -eq 1) {
        $tmpdirGuard.Clauses[0].Item2
    }
    else { $null }
    $tmpdirThrow = if (
        $null -ne $tmpdirGuardBody -and
        @($tmpdirGuardBody.Statements).Count -eq 1 -and
        $null -ne $tmpdirGuardBody.Statements[0] -and
        $tmpdirGuardBody.Statements[0] -is
            [System.Management.Automation.Language.ThrowStatementAst]) {
        $tmpdirGuardBody.Statements[0]
    }
    else { $null }
    $tmpdirError = if (
        $null -ne $tmpdirThrow -and
        $tmpdirThrow.Pipeline -is [System.Management.Automation.Language.PipelineAst] -and
        @($tmpdirThrow.Pipeline.PipelineElements).Count -eq 1 -and
        $tmpdirThrow.Pipeline.PipelineElements[0] -is
            [System.Management.Automation.Language.CommandExpressionAst]) {
        $tmpdirThrow.Pipeline.PipelineElements[0].Expression
    }
    else { $null }
    if (-not (& $isStaticInvocation $tmpdirGuardCondition 'string' 'Equals') -or
        @($tmpdirGuardCondition.Arguments).Count -ne 3 -or
        -not (& $isStringCastOfVariable $tmpdirGuardCondition.Arguments[0] 'name') -or
        -not (& $isConstantString $tmpdirGuardCondition.Arguments[1] 'TMPDIR') -or
        -not (& $isStaticMember `
            $tmpdirGuardCondition.Arguments[2] 'StringComparison' 'OrdinalIgnoreCase') -or
        $null -ne $tmpdirGuardBody.Traps -or
        -not (& $isConstantString $tmpdirError `
            'EDGE-SPLIT-AUTHORITY-DEV-TEMP child environment overlay must not replace process TMPDIR.')) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 Invoke-DevProcess TMPDIR overlay rejection changed.'
    }

    $environmentOverlayAssignment = $environmentForEach.Body.Statements[1]
    $overlayRightCast = if (
        $environmentOverlayAssignment.Right -is
            [System.Management.Automation.Language.CommandExpressionAst]) {
        $environmentOverlayAssignment.Right.Expression
    }
    else { $null }
    $overlaySourceIndex = if (
        $overlayRightCast -is [System.Management.Automation.Language.ConvertExpressionAst] -and
        [string]::Equals(
            [string]$overlayRightCast.Type.TypeName.FullName, 'string',
            [StringComparison]::OrdinalIgnoreCase) -and
        $overlayRightCast.Child -is [System.Management.Automation.Language.IndexExpressionAst]) {
        $overlayRightCast.Child
    }
    else { $null }
    if ($environmentOverlayAssignment.Operator -ne
            [System.Management.Automation.Language.TokenKind]::Equals -or
        $environmentOverlayAssignment.Left -isnot
            [System.Management.Automation.Language.IndexExpressionAst] -or
        -not (& $isInstanceMember `
            $environmentOverlayAssignment.Left.Target 'startInfo' 'Environment') -or
        -not (& $isStringCastOfVariable `
            $environmentOverlayAssignment.Left.Index 'name') -or
        $null -eq $overlaySourceIndex -or
        -not (& $isVariable $overlaySourceIndex.Target 'Environment') -or
        -not (& $isVariable $overlaySourceIndex.Index 'name')) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 Invoke-DevProcess environment overlay assignment changed.'
    }

    $environmentCollectionMembers = @($invokeDevProcess.Body.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.MemberExpressionAst] -and
                $node.Member -is
                    [System.Management.Automation.Language.StringConstantExpressionAst] -and
                ([string]::Equals(
                        [string]$node.Member.Value, 'Environment',
                        [StringComparison]::OrdinalIgnoreCase) -or
                    [string]::Equals(
                        [string]$node.Member.Value, 'EnvironmentVariables',
                        [StringComparison]::OrdinalIgnoreCase))
            }, $true))
    $environmentSourceIndexes = @($invokeDevProcess.Body.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.IndexExpressionAst] -and
                $node.Target -is
                    [System.Management.Automation.Language.VariableExpressionAst] -and
                [string]::Equals(
                    [string]$node.Target.VariablePath.UserPath, 'Environment',
                    [StringComparison]::OrdinalIgnoreCase)
            }, $true))
    if ($environmentCollectionMembers.Count -ne 1 -or
        -not [object]::ReferenceEquals(
            $environmentCollectionMembers[0], $environmentOverlayAssignment.Left.Target) -or
        $environmentSourceIndexes.Count -ne 1 -or
        -not [object]::ReferenceEquals($environmentSourceIndexes[0], $overlaySourceIndex)) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 Invoke-DevProcess environment collection ownership changed.'
    }

    $forbiddenEnvironmentMutators = @($invokeDevProcess.Body.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.InvokeMemberExpressionAst] -and
                $node.Member -is
                    [System.Management.Automation.Language.StringConstantExpressionAst] -and
                ([string]::Equals(
                        [string]$node.Member.Value, 'Clear',
                        [StringComparison]::OrdinalIgnoreCase) -or
                    [string]::Equals(
                        [string]$node.Member.Value, 'Remove',
                        [StringComparison]::OrdinalIgnoreCase))
            }, $true))
    $invokeEnvironmentStaticCalls = @($invokeDevProcess.Body.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.InvokeMemberExpressionAst] -and
                $node.Static -and
                $node.Expression -is [System.Management.Automation.Language.TypeExpressionAst] -and
                ([string]::Equals(
                        [string]$node.Expression.TypeName.FullName, 'Environment',
                        [StringComparison]::OrdinalIgnoreCase) -or
                    [string]::Equals(
                        [string]$node.Expression.TypeName.FullName, 'System.Environment',
                        [StringComparison]::OrdinalIgnoreCase))
            }, $true))
    $invokeEnvironmentVariableWrites = @($invokeDevProcess.Body.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
                $node.Left -is [System.Management.Automation.Language.VariableExpressionAst] -and
                ([string]$node.Left.VariablePath.UserPath).StartsWith(
                    'env:', [StringComparison]::OrdinalIgnoreCase)
            }, $true))
    $forbiddenEnvironmentCommands = @(
        'Set-Item', 'si', 'Clear-Item', 'cli', 'Remove-Item', 'ri',
        'Set-ItemProperty', 'sp', 'Remove-ItemProperty', 'rp',
        'New-ItemProperty', 'Set-Variable', 'sv', 'Remove-Variable', 'rv')
    $invokeForbiddenCommands = @($invokeDevProcess.Body.FindAll({
                param($node) $node -is [System.Management.Automation.Language.CommandAst]
            }, $true) | Where-Object {
                $commandName = [string]$_.GetCommandName()
                @($forbiddenEnvironmentCommands | Where-Object {
                        [string]::Equals(
                            $_, $commandName, [StringComparison]::OrdinalIgnoreCase)
                    }).Count -ne 0
            })
    $invokeTmpdirStrings = @($invokeDevProcess.Body.FindAll({
                param($node)
                ($node -is [System.Management.Automation.Language.StringConstantExpressionAst] -or
                    $node -is [System.Management.Automation.Language.ExpandableStringExpressionAst]) -and
                $null -ne $node.Value -and
                ([string]$node.Value).IndexOf(
                    'TMPDIR', [StringComparison]::OrdinalIgnoreCase) -ge 0
            }, $true))
    if ($forbiddenEnvironmentMutators.Count -ne 0 -or
        $invokeEnvironmentStaticCalls.Count -ne 0 -or
        $invokeEnvironmentVariableWrites.Count -ne 0 -or
        $invokeForbiddenCommands.Count -ne 0 -or
        $invokeTmpdirStrings.Count -ne 2 -or
        -not ($invokeTmpdirStrings -contains $tmpdirGuardCondition.Arguments[1]) -or
        -not ($invokeTmpdirStrings -contains $tmpdirError)) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 Invoke-DevProcess contains a forbidden environment mutation path.'
    }

    $helpers = @($developmentAst.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
                [string]::Equals(
                    [string]$node.Name, 'Resolve-DevPhysicalTempRoot',
                    [StringComparison]::OrdinalIgnoreCase)
            }, $true))
    if ($helpers.Count -ne 1 -or
        -not [object]::ReferenceEquals($helpers[0].Parent, $developmentAst.EndBlock)) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 development validation must define exactly one top-level physical temp helper.'
    }
    $helper = $helpers[0]

    $allowedHelperCommands = [ordered]@{ 'Test-Path' = 2; 'Get-Item' = 2 }
    $actualHelperCommands = @{}
    $helperCommands = @($helper.Body.FindAll({
                param($node) $node -is [System.Management.Automation.Language.CommandAst]
            }, $true))
    foreach ($command in $helperCommands) {
        $name = [string]$command.GetCommandName()
        if ([string]::IsNullOrWhiteSpace($name) -or -not $allowedHelperCommands.Contains($name)) {
            throw "EDGE-SPLIT-AUTHORITY-STATIC-001 physical temp helper command '$name' is forbidden; realpath/readlink/Resolve-Path/process/external commands are not allowed."
        }
        $actualHelperCommands[$name] = 1 + [int]$actualHelperCommands[$name]
    }
    foreach ($name in $allowedHelperCommands.Keys) {
        if ([int]$actualHelperCommands[$name] -ne [int]$allowedHelperCommands[$name]) {
            throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 physical temp helper command allowlist count changed.'
        }
    }

    $allowedHelperInvocations = [ordered]@{
        'IO.Path::GetFullPath' = 2
        'IO.Path::GetTempPath' = 1
        'string::IsNullOrWhiteSpace' = 2
        'string::Equals' = 1
    }
    $actualHelperInvocations = @{}
    $helperInvocations = @($helper.Body.FindAll({
                param($node) $node -is [System.Management.Automation.Language.InvokeMemberExpressionAst]
            }, $true))
    foreach ($invocation in $helperInvocations) {
        if (-not $invocation.Static -or
            $invocation.Expression -isnot [System.Management.Automation.Language.TypeExpressionAst] -or
            $invocation.Member -isnot [System.Management.Automation.Language.StringConstantExpressionAst]) {
            throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 physical temp helper contains a dynamic/member process or external invocation.'
        }
        $key = '{0}::{1}' -f [string]$invocation.Expression.TypeName.FullName, [string]$invocation.Member.Value
        if (-not $allowedHelperInvocations.Contains($key)) {
            throw "EDGE-SPLIT-AUTHORITY-STATIC-001 physical temp helper member invocation '$key' is outside the minimal allowlist."
        }
        $actualHelperInvocations[$key] = 1 + [int]$actualHelperInvocations[$key]
    }
    foreach ($key in $allowedHelperInvocations.Keys) {
        if ([int]$actualHelperInvocations[$key] -ne [int]$allowedHelperInvocations[$key]) {
            throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 physical temp helper member invocation allowlist count changed.'
        }
    }

    $currentDirectoryTries = @($helper.Body.EndBlock.Statements | Where-Object {
            $_ -is [System.Management.Automation.Language.TryStatementAst] -and $null -ne $_.Finally
        })
    if ($currentDirectoryTries.Count -ne 1) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 physical temp helper lost its direct try/finally.'
    }
    $currentDirectoryTry = $currentDirectoryTries[0]
    if (@($currentDirectoryTry.CatchClauses).Count -ne 1 -or
        -not (& $testDirectFailureCatch $currentDirectoryTry.CatchClauses[0] 'currentDirectoryFailure')) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 physical temp helper body catch no longer binds its first failure.'
    }

    $currentDirectoryAssignments = @($helper.Body.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
                $node.Left -is [System.Management.Automation.Language.MemberExpressionAst] -and
                $node.Left -isnot [System.Management.Automation.Language.InvokeMemberExpressionAst] -and
                $node.Left.Static -and
                $node.Left.Expression -is [System.Management.Automation.Language.TypeExpressionAst] -and
                [string]::Equals(
                    [string]$node.Left.Expression.TypeName.FullName, 'Environment',
                    [StringComparison]::OrdinalIgnoreCase) -and
                [string]::Equals(
                    [string]$node.Left.Member.Value, 'CurrentDirectory',
                    [StringComparison]::OrdinalIgnoreCase)
            }, $true))
    if ($currentDirectoryAssignments.Count -ne 2) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 physical temp helper must set and restore CurrentDirectory exactly once.'
    }
    $setCurrentDirectory = @($currentDirectoryAssignments | Where-Object {
            $_.Right -is [System.Management.Automation.Language.CommandExpressionAst] -and
            (& $isVariable $_.Right.Expression 'temporaryRoot')
        })
    $restoreCurrentDirectory = @($currentDirectoryAssignments | Where-Object {
            $_.Right -is [System.Management.Automation.Language.CommandExpressionAst] -and
            (& $isVariable $_.Right.Expression 'originalCurrentDirectory')
        })
    if ($setCurrentDirectory.Count -ne 1 -or $restoreCurrentDirectory.Count -ne 1 -or
        -not (& $isWithin $setCurrentDirectory[0] $currentDirectoryTry.Body) -or
        -not (& $isWithin $restoreCurrentDirectory[0] $currentDirectoryTry.Finally)) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 CurrentDirectory set/restore escaped the helper try/finally.'
    }

    $temporaryRootAssignments = @(& $getVariableAssignments $helper.Body 'temporaryRoot')
    $originalCurrentDirectoryAssignments = @(& $getVariableAssignments $helper.Body 'originalCurrentDirectory')
    $physicalRootAssignments = @(& $getVariableAssignments $helper.Body 'physicalRoot')
    if ($temporaryRootAssignments.Count -ne 1 -or
        $originalCurrentDirectoryAssignments.Count -ne 1 -or
        $physicalRootAssignments.Count -ne 1) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 physical temp helper dataflow variable assignment count changed.'
    }
    $temporaryRootExpression = $temporaryRootAssignments[0].Right
    $temporaryRootGetFullPath = if (
        $temporaryRootExpression -is [System.Management.Automation.Language.CommandExpressionAst]) {
        $temporaryRootExpression.Expression
    }
    else { $null }
    if (-not (& $isStaticInvocation $temporaryRootGetFullPath 'IO.Path' 'GetFullPath') -or
        @($temporaryRootGetFullPath.Arguments).Count -ne 1 -or
        -not (& $isStaticInvocation $temporaryRootGetFullPath.Arguments[0] 'IO.Path' 'GetTempPath') -or
        $null -ne $temporaryRootGetFullPath.Arguments[0].Arguments -or
        -not [object]::ReferenceEquals($temporaryRootAssignments[0].Parent, $helper.Body.EndBlock)) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 temporaryRoot must be the direct GetFullPath(GetTempPath()) assignment.'
    }
    if ($originalCurrentDirectoryAssignments[0].Right -isnot
            [System.Management.Automation.Language.CommandExpressionAst] -or
        -not (& $isStaticMember `
            $originalCurrentDirectoryAssignments[0].Right.Expression 'Environment' 'CurrentDirectory') -or
        -not [object]::ReferenceEquals(
            $originalCurrentDirectoryAssignments[0].Parent, $helper.Body.EndBlock)) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 originalCurrentDirectory must have one direct canonical save.'
    }
    $physicalRootReads = @($physicalRootAssignments | Where-Object {
            $_.Right -is [System.Management.Automation.Language.CommandExpressionAst] -and
            (& $isStaticInvocation $_.Right.Expression 'IO.Path' 'GetFullPath') -and
            @($_.Right.Expression.Arguments).Count -eq 1 -and
            (& $isStaticMember $_.Right.Expression.Arguments[0] 'Environment' 'CurrentDirectory')
        })
    if ($physicalRootReads.Count -ne 1 -or
        -not [object]::ReferenceEquals($physicalRootReads[0].Parent, $currentDirectoryTry.Body) -or
        $temporaryRootAssignments[0].Extent.StartOffset -ge
            $originalCurrentDirectoryAssignments[0].Extent.StartOffset -or
        $originalCurrentDirectoryAssignments[0].Extent.StartOffset -ge $currentDirectoryTry.Extent.StartOffset -or
        $setCurrentDirectory[0].Extent.StartOffset -ge $physicalRootReads[0].Extent.StartOffset) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 CurrentDirectory physical read is not bound to the saved direct helper flow.'
    }
    $helperReturns = @($helper.Body.FindAll({
                param($node) $node -is [System.Management.Automation.Language.ReturnStatementAst]
            }, $true))
    if ($helperReturns.Count -ne 1 -or
        -not [object]::ReferenceEquals($helperReturns[0].Parent, $helper.Body.EndBlock) -or
        $helperReturns[0].Pipeline -isnot [System.Management.Automation.Language.PipelineAst] -or
        @($helperReturns[0].Pipeline.PipelineElements).Count -ne 1 -or
        $helperReturns[0].Pipeline.PipelineElements[0] -isnot
            [System.Management.Automation.Language.CommandExpressionAst] -or
        -not (& $isVariable $helperReturns[0].Pipeline.PipelineElements[0].Expression 'physicalRoot')) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 physical temp helper must return only the canonical physicalRoot.'
    }

    $restoreTries = @($currentDirectoryTry.Finally.Statements | Where-Object {
            $_ -is [System.Management.Automation.Language.TryStatementAst]
        })
    if ($restoreTries.Count -ne 1 -or @($restoreTries[0].CatchClauses).Count -ne 1 -or
        $null -ne $restoreTries[0].Finally -or
        -not (& $isWithin $restoreCurrentDirectory[0] $restoreTries[0].Body) -or
        -not (& $testGuardedFailureCatch $restoreTries[0].CatchClauses[0] 'currentDirectoryFailure')) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 CurrentDirectory restore lost its guarded first-failure catch.'
    }
    $restoreEqualityCalls = @($restoreTries[0].Body.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.InvokeMemberExpressionAst] -and
                $node.Static -and
                [string]::Equals(
                    [string]$node.Expression.TypeName.FullName, 'string',
                    [StringComparison]::OrdinalIgnoreCase) -and
                [string]::Equals(
                    [string]$node.Member.Value, 'Equals',
                    [StringComparison]::OrdinalIgnoreCase)
            }, $true))
    if ($restoreEqualityCalls.Count -ne 1 -or
        @($restoreEqualityCalls[0].Arguments).Count -ne 3 -or
        -not (& $isStaticMember $restoreEqualityCalls[0].Arguments[0] 'Environment' 'CurrentDirectory') -or
        -not (& $isVariable $restoreEqualityCalls[0].Arguments[1] 'originalCurrentDirectory') -or
        -not (& $isStaticMember $restoreEqualityCalls[0].Arguments[2] 'StringComparison' 'Ordinal') -or
        $restoreCurrentDirectory[0].Extent.StartOffset -ge $restoreEqualityCalls[0].Extent.StartOffset) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 CurrentDirectory restore no longer has exact post-restore verification.'
    }

    $topLevelOuterTries = @($developmentAst.EndBlock.Statements | Where-Object {
            $_ -is [System.Management.Automation.Language.TryStatementAst] -and $null -ne $_.Finally
        })
    if ($topLevelOuterTries.Count -ne 1) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 development validation lost its top-level TMPDIR try/finally.'
    }
    $outerTry = $topLevelOuterTries[0]
    if (@($outerTry.CatchClauses).Count -ne 1 -or
        -not (& $testDirectFailureCatch $outerTry.CatchClauses[0] 'tmpDirectoryFailure')) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 development TMPDIR body catch no longer binds its first failure.'
    }

    $physicalTempAssignments = @(& $getVariableAssignments $developmentAst 'physicalTempRoot')
    $physicalTempInvocations = @($developmentAst.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.CommandAst] -and
                [string]::Equals(
                    [string]$node.GetCommandName(), 'Resolve-DevPhysicalTempRoot',
                    [StringComparison]::OrdinalIgnoreCase)
            }, $true))
    $physicalTempPipeline = if ($physicalTempAssignments.Count -eq 1) {
        $physicalTempAssignments[0].Right
    }
    else { $null }
    if ($physicalTempAssignments.Count -ne 1 -or
        -not [object]::ReferenceEquals(
            $physicalTempAssignments[0].Parent, $developmentAst.EndBlock) -or
        $physicalTempPipeline -isnot [System.Management.Automation.Language.PipelineAst] -or
        @($physicalTempPipeline.PipelineElements).Count -ne 1 -or
        $physicalTempPipeline.PipelineElements[0] -isnot
            [System.Management.Automation.Language.CommandAst] -or
        @($physicalTempPipeline.PipelineElements[0].CommandElements).Count -ne 1 -or
        $physicalTempInvocations.Count -ne 1 -or
        -not [object]::ReferenceEquals(
            $physicalTempInvocations[0], $physicalTempPipeline.PipelineElements[0]) -or
        $physicalTempAssignments[0].Extent.StartOffset -ge $outerTry.Extent.StartOffset) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 physical temp helper invocation is not the single pre-pin root assignment.'
    }

    $environmentAssignmentNames = @(
        'tmpDirectoryEnvironment', 'tmpDirectoryPinnedEnvironment', 'tmpDirectoryRestorationEnvironment')
    $environmentAssignments = @{}
    foreach ($name in $environmentAssignmentNames) {
        $assignments = @(& $getVariableAssignments $developmentAst $name)
        if ($assignments.Count -ne 1 -or
            -not (& $testProcessEnvironmentAssignment $assignments[0])) {
            throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 TMPDIR state snapshots must use process-scope environment reads.'
        }
        $environmentAssignments[$name] = $assignments[0]
    }
    if (-not [object]::ReferenceEquals(
            $environmentAssignments['tmpDirectoryEnvironment'].Parent, $developmentAst.EndBlock) -or
        $environmentAssignments['tmpDirectoryEnvironment'].Extent.StartOffset -ge
            $physicalTempAssignments[0].Extent.StartOffset -or
        -not [object]::ReferenceEquals(
            $environmentAssignments['tmpDirectoryPinnedEnvironment'].Parent, $outerTry.Body) -or
        -not (& $isWithin $environmentAssignments['tmpDirectoryRestorationEnvironment'] $outerTry.Finally)) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 TMPDIR process snapshots escaped capture/pin/restore regions.'
    }

    $tmpDirectoryWasPresentAssignments = @(& $getVariableAssignments `
        $developmentAst 'tmpDirectoryWasPresent')
    $tmpDirectoryOriginalValueAssignments = @(& $getVariableAssignments `
        $developmentAst 'tmpDirectoryOriginalValue')
    $tmpDirectoryPresenceCall = if (
        $tmpDirectoryWasPresentAssignments.Count -eq 1 -and
        $tmpDirectoryWasPresentAssignments[0].Right -is
            [System.Management.Automation.Language.CommandExpressionAst]) {
        $tmpDirectoryWasPresentAssignments[0].Right.Expression
    }
    else { $null }
    if ($tmpDirectoryWasPresentAssignments.Count -ne 1 -or
        -not [object]::ReferenceEquals(
            $tmpDirectoryWasPresentAssignments[0].Parent, $developmentAst.EndBlock) -or
        $tmpDirectoryPresenceCall -isnot
            [System.Management.Automation.Language.InvokeMemberExpressionAst] -or
        $tmpDirectoryPresenceCall.Static -or
        -not (& $isVariable `
            $tmpDirectoryPresenceCall.Expression 'tmpDirectoryEnvironment') -or
        $tmpDirectoryPresenceCall.Member -isnot
            [System.Management.Automation.Language.StringConstantExpressionAst] -or
        -not [string]::Equals(
            [string]$tmpDirectoryPresenceCall.Member.Value, 'Contains',
            [StringComparison]::OrdinalIgnoreCase) -or
        @($tmpDirectoryPresenceCall.Arguments).Count -ne 1 -or
        -not (& $isConstantString $tmpDirectoryPresenceCall.Arguments[0] 'TMPDIR')) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 original TMPDIR presence capture changed.'
    }

    $tmpDirectoryOriginalIf = if ($tmpDirectoryOriginalValueAssignments.Count -eq 1) {
        $tmpDirectoryOriginalValueAssignments[0].Right
    }
    else { $null }
    $tmpDirectoryOriginalTrueExpression = if (
        $tmpDirectoryOriginalIf -is [System.Management.Automation.Language.IfStatementAst] -and
        @($tmpDirectoryOriginalIf.Clauses).Count -eq 1 -and
        $null -ne $tmpDirectoryOriginalIf.ElseClause -and
        (& $testSingleVariableCondition `
            $tmpDirectoryOriginalIf.Clauses[0].Item1 'tmpDirectoryWasPresent') -and
        @($tmpDirectoryOriginalIf.Clauses[0].Item2.Statements).Count -eq 1 -and
        $tmpDirectoryOriginalIf.Clauses[0].Item2.Statements[0] -is
            [System.Management.Automation.Language.PipelineAst] -and
        @($tmpDirectoryOriginalIf.Clauses[0].Item2.Statements[0].PipelineElements).Count -eq 1 -and
        $tmpDirectoryOriginalIf.Clauses[0].Item2.Statements[0].PipelineElements[0] -is
            [System.Management.Automation.Language.CommandExpressionAst]) {
        $tmpDirectoryOriginalIf.Clauses[0].Item2.Statements[0].PipelineElements[0].Expression
    }
    else { $null }
    $tmpDirectoryOriginalIndex = if (
        $tmpDirectoryOriginalTrueExpression -is
            [System.Management.Automation.Language.ConvertExpressionAst] -and
        [string]::Equals(
            [string]$tmpDirectoryOriginalTrueExpression.Type.TypeName.FullName,
            'string', [StringComparison]::OrdinalIgnoreCase) -and
        $tmpDirectoryOriginalTrueExpression.Child -is
            [System.Management.Automation.Language.IndexExpressionAst]) {
        $tmpDirectoryOriginalTrueExpression.Child
    }
    else { $null }
    $tmpDirectoryOriginalElseExpression = if (
        $tmpDirectoryOriginalIf -is [System.Management.Automation.Language.IfStatementAst] -and
        $null -ne $tmpDirectoryOriginalIf.ElseClause -and
        @($tmpDirectoryOriginalIf.ElseClause.Statements).Count -eq 1 -and
        $tmpDirectoryOriginalIf.ElseClause.Statements[0] -is
            [System.Management.Automation.Language.PipelineAst] -and
        @($tmpDirectoryOriginalIf.ElseClause.Statements[0].PipelineElements).Count -eq 1 -and
        $tmpDirectoryOriginalIf.ElseClause.Statements[0].PipelineElements[0] -is
            [System.Management.Automation.Language.CommandExpressionAst]) {
        $tmpDirectoryOriginalIf.ElseClause.Statements[0].PipelineElements[0].Expression
    }
    else { $null }
    if ($tmpDirectoryOriginalValueAssignments.Count -ne 1 -or
        -not [object]::ReferenceEquals(
            $tmpDirectoryOriginalValueAssignments[0].Parent, $developmentAst.EndBlock) -or
        $null -eq $tmpDirectoryOriginalIndex -or
        -not (& $isVariable `
            $tmpDirectoryOriginalIndex.Target 'tmpDirectoryEnvironment') -or
        -not (& $isConstantString $tmpDirectoryOriginalIndex.Index 'TMPDIR') -or
        -not (& $isVariable $tmpDirectoryOriginalElseExpression 'null') -or
        $environmentAssignments['tmpDirectoryEnvironment'].Extent.StartOffset -ge
            $tmpDirectoryWasPresentAssignments[0].Extent.StartOffset -or
        $tmpDirectoryWasPresentAssignments[0].Extent.StartOffset -ge
            $tmpDirectoryOriginalValueAssignments[0].Extent.StartOffset -or
        $tmpDirectoryOriginalValueAssignments[0].Extent.StartOffset -ge
            $physicalTempAssignments[0].Extent.StartOffset) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 original TMPDIR value capture changed or was overwritten.'
    }

    $snapshotContainsCalls = @{}
    $snapshotIndexReads = @{}
    foreach ($snapshotName in $environmentAssignmentNames) {
        $snapshotMembers = @($developmentAst.FindAll({
                    param($node)
                    $node -is [System.Management.Automation.Language.MemberExpressionAst] -and
                    $node.Expression -is
                        [System.Management.Automation.Language.VariableExpressionAst]
                }, $true) | Where-Object {
                    & $matchesVariableName $_.Expression $snapshotName
                })
        $snapshotIndexes = @($developmentAst.FindAll({
                    param($node)
                    $node -is [System.Management.Automation.Language.IndexExpressionAst] -and
                    $node.Target -is
                        [System.Management.Automation.Language.VariableExpressionAst]
                }, $true) | Where-Object {
                    & $matchesVariableName $_.Target $snapshotName
                })
        $snapshotVariables = @($developmentAst.FindAll({
                    param($node)
                    $node -is [System.Management.Automation.Language.VariableExpressionAst]
                }, $true) | Where-Object {
                    & $matchesVariableName $_ $snapshotName
                })
        $containsCalls = @($snapshotMembers | Where-Object {
                $_ -is [System.Management.Automation.Language.InvokeMemberExpressionAst] -and
                -not $_.Static -and
                $_.Member -is
                    [System.Management.Automation.Language.StringConstantExpressionAst] -and
                [string]::Equals(
                    [string]$_.Member.Value, 'Contains',
                    [StringComparison]::OrdinalIgnoreCase) -and
                @($_.Arguments).Count -eq 1 -and
                (& $isConstantString $_.Arguments[0] 'TMPDIR')
            })
        if ($snapshotMembers.Count -ne 1 -or $containsCalls.Count -ne 1 -or
            $snapshotIndexes.Count -ne 1 -or
            -not (& $isConstantString $snapshotIndexes[0].Index 'TMPDIR') -or
            $snapshotIndexes[0].Parent -isnot
                [System.Management.Automation.Language.ConvertExpressionAst] -or
            -not [string]::Equals(
                [string]$snapshotIndexes[0].Parent.Type.TypeName.FullName,
                'string', [StringComparison]::OrdinalIgnoreCase) -or
            $snapshotVariables.Count -ne 3) {
            throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 TMPDIR snapshot dictionary read inventory changed.'
        }
        $expectedSnapshotVariables = @(
            $environmentAssignments[$snapshotName].Left,
            $containsCalls[0].Expression,
            $snapshotIndexes[0].Target)
        foreach ($variable in $snapshotVariables) {
            if (@($expectedSnapshotVariables | Where-Object {
                        [object]::ReferenceEquals($_, $variable)
                    }).Count -ne 1) {
                throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 TMPDIR snapshot dictionary escaped its exact read-only dataflow.'
            }
        }
        $snapshotContainsCalls[$snapshotName] = $containsCalls[0]
        $snapshotIndexReads[$snapshotName] = $snapshotIndexes[0]
    }

    $environmentStaticInvocations = @($developmentAst.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.InvokeMemberExpressionAst] -and
                $node.Static -and
                $node.Expression -is [System.Management.Automation.Language.TypeExpressionAst] -and
                ([string]::Equals(
                        [string]$node.Expression.TypeName.FullName, 'Environment',
                        [StringComparison]::OrdinalIgnoreCase) -or
                    [string]::Equals(
                        [string]$node.Expression.TypeName.FullName, 'System.Environment',
                        [StringComparison]::OrdinalIgnoreCase))
            }, $true))
    $environmentInvocationCounts = @{}
    foreach ($invocation in $environmentStaticInvocations) {
        if ($invocation.Member -isnot
                [System.Management.Automation.Language.StringConstantExpressionAst] -or
            -not (@('GetEnvironmentVariables', 'SetEnvironmentVariable') -contains
                [string]$invocation.Member.Value)) {
            throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 development validation contains a computed or forbidden Environment invocation.'
        }
        $memberName = [string]$invocation.Member.Value
        $environmentInvocationCounts[$memberName] =
            1 + [int]$environmentInvocationCounts[$memberName]
    }
    if ($environmentStaticInvocations.Count -ne 5 -or
        [int]$environmentInvocationCounts['GetEnvironmentVariables'] -ne 3 -or
        [int]$environmentInvocationCounts['SetEnvironmentVariable'] -ne 2) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 process Environment invocation inventory changed.'
    }

    $allDevelopmentCommands = @($developmentAst.FindAll({
                param($node) $node -is [System.Management.Automation.Language.CommandAst]
            }, $true))
    $dynamicDevelopmentCommands = @($allDevelopmentCommands | Where-Object {
            [string]::IsNullOrWhiteSpace([string]$_.GetCommandName()) -or
            $_.InvocationOperator -ne
                [System.Management.Automation.Language.TokenKind]::Unknown
        })
    $forbiddenCommandLeafNames = @(
        'Set-Alias', 'sal', 'New-Alias', 'nal', 'Remove-Alias',
        'Set-Item', 'si', 'Clear-Item', 'cli',
        'Remove-Item', 'ri', 'rm', 'del', 'erase', 'rd', 'rmdir',
        'New-Item', 'ni',
        'Set-ItemProperty', 'sp', 'Remove-ItemProperty', 'rp',
        'New-ItemProperty', 'Set-Variable', 'sv', 'New-Variable', 'nv',
        'Clear-Variable', 'clv', 'Remove-Variable', 'rv',
        'Set-Content', 'sc', 'Clear-Content', 'clc', 'Add-Content', 'ac',
        'Out-File', 'Rename-Item', 'rni', 'Move-Item', 'mi', 'mv',
        'Copy-Item', 'cpi', 'cp', 'Invoke-Expression', 'iex', 'New-Module',
        'Add-Type')
    $forbiddenCommandLeafNames += @(
        'Start-Process', 'saps', 'start', 'New-Object')
    $forbiddenDevelopmentCommands = @($allDevelopmentCommands | Where-Object {
            $commandName = [string]$_.GetCommandName()
            $leafName = @($commandName -split '\\')[-1]
            @($forbiddenCommandLeafNames | Where-Object {
                    [string]::Equals(
                        $_, $leafName, [StringComparison]::OrdinalIgnoreCase)
                }).Count -ne 0 -and
                -not [string]::Equals(
                    $commandName, 'Remove-Item', [StringComparison]::OrdinalIgnoreCase) -and
                -not [string]::Equals(
                    $commandName, 'New-Item', [StringComparison]::OrdinalIgnoreCase)
        })
    $providerVariableExpressions = @($developmentAst.FindAll({
                param($node)
                if ($node -isnot
                    [System.Management.Automation.Language.VariableExpressionAst]) {
                    return $false
                }
                $name = [string]$node.VariablePath.UserPath
                return $name.StartsWith(
                        'env:', [StringComparison]::OrdinalIgnoreCase) -or
                    $name.StartsWith(
                        'function:', [StringComparison]::OrdinalIgnoreCase) -or
                    $name.StartsWith(
                        'alias:', [StringComparison]::OrdinalIgnoreCase)
            }, $true))
    $dynamicDevelopmentMemberInvocations = @($developmentAst.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.InvokeMemberExpressionAst] -and
                ($node.Member -isnot
                        [System.Management.Automation.Language.StringConstantExpressionAst] -or
                    @(
                        'Set', 'Clear', 'Remove', 'Invoke', 'InvokeScript',
                        'CreateInstance', 'CreateInstanceFrom') -contains
                        [string]$node.Member.Value)
            }, $true))
    $setEnvironmentVariableMembers = @($developmentAst.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.InvokeMemberExpressionAst] -and
                $node.Member -is
                    [System.Management.Automation.Language.StringConstantExpressionAst] -and
                [string]::Equals(
                    [string]$node.Member.Value, 'SetEnvironmentVariable',
                    [StringComparison]::OrdinalIgnoreCase)
            }, $true))
    $getEnvironmentVariablesMembers = @($developmentAst.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.InvokeMemberExpressionAst] -and
                $node.Member -is
                    [System.Management.Automation.Language.StringConstantExpressionAst] -and
                [string]::Equals(
                    [string]$node.Member.Value, 'GetEnvironmentVariables',
                    [StringComparison]::OrdinalIgnoreCase)
            }, $true))
    $importModuleCommands = @($allDevelopmentCommands | Where-Object {
            [string]::Equals(
                [string]$_.GetCommandName(), 'Import-Module',
                [StringComparison]::OrdinalIgnoreCase)
        })
    if ($dynamicDevelopmentCommands.Count -ne 0 -or
        $forbiddenDevelopmentCommands.Count -ne 0 -or
        $providerVariableExpressions.Count -ne 0 -or
        $dynamicDevelopmentMemberInvocations.Count -ne 0 -or
        $setEnvironmentVariableMembers.Count -ne 2 -or
        $getEnvironmentVariablesMembers.Count -ne 3 -or
        $importModuleCommands.Count -ne 2) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 development command/member owner inventory permits a dynamic, alias, or provider rebinding path.'
    }
    foreach ($importVariable in @('staticGuardModulePath', 'protocolModulePath')) {
        $matchingImports = @($importModuleCommands | Where-Object {
                $elements = @($_.CommandElements)
                $elements.Count -eq 3 -and
                (& $isVariable $elements[1] $importVariable) -and
                $elements[2] -is
                    [System.Management.Automation.Language.CommandParameterAst] -and
                [string]::Equals(
                    [string]$elements[2].ParameterName, 'Force',
                    [StringComparison]::OrdinalIgnoreCase)
            })
        if ($matchingImports.Count -ne 1) {
            throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 development canonical module imports changed.'
        }
    }

    $removeItemLeafCommands = @($allDevelopmentCommands | Where-Object {
            $commandName = [string]$_.GetCommandName()
            [string]::Equals(
                [string](@($commandName -split '\\')[-1]), 'Remove-Item',
                [StringComparison]::OrdinalIgnoreCase)
        })
    $newItemLeafCommands = @($allDevelopmentCommands | Where-Object {
            $commandName = [string]$_.GetCommandName()
            [string]::Equals(
                [string](@($commandName -split '\\')[-1]), 'New-Item',
                [StringComparison]::OrdinalIgnoreCase)
        })
    $allRemoveItemCommands = @($removeItemLeafCommands | Where-Object {
            [string]::Equals(
                [string]$_.GetCommandName(), 'Remove-Item',
                [StringComparison]::OrdinalIgnoreCase)
        })
    $allNewItemCommands = @($newItemLeafCommands | Where-Object {
            [string]::Equals(
                [string]$_.GetCommandName(), 'New-Item',
                [StringComparison]::OrdinalIgnoreCase)
        })
    $oldDestinationRemovals = @($allRemoveItemCommands | Where-Object {
            & $testCanonicalFileRemoval $_ 'oldDestination'
        })
    $destinationRemovals = @($allRemoveItemCommands | Where-Object {
            & $testCanonicalFileRemoval $_ 'destination'
        })
    $snapshotDirectoryCreations = @($allNewItemCommands | Where-Object {
            & $testCanonicalDirectoryCreation $_ 'destination' $true
        })
    $outerDirectoryCreations = @($allNewItemCommands | Where-Object {
            & $testCanonicalDirectoryCreation $_ 'outerRunRoot' $false
        })
    $evidenceDirectoryCreations = @($allNewItemCommands | Where-Object {
            & $testCanonicalDirectoryCreation $_ 'evidencePath' $true
        })
    if ($removeItemLeafCommands.Count -ne 3 -or
        $allRemoveItemCommands.Count -ne 3 -or
        $oldDestinationRemovals.Count -ne 1 -or
        $destinationRemovals.Count -ne 1 -or
        $newItemLeafCommands.Count -ne 3 -or
        $allNewItemCommands.Count -ne 3 -or
        $snapshotDirectoryCreations.Count -ne 1 -or
        $outerDirectoryCreations.Count -ne 1 -or
        $evidenceDirectoryCreations.Count -ne 1) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 provider-capable item command inventory changed.'
    }

    $tmpDirectorySetCalls = @($developmentAst.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.InvokeMemberExpressionAst] -and
                $node.Static -and
                $node.Expression -is [System.Management.Automation.Language.TypeExpressionAst] -and
                [string]::Equals(
                    [string]$node.Expression.TypeName.FullName, 'Environment',
                    [StringComparison]::OrdinalIgnoreCase) -and
                [string]::Equals(
                    [string]$node.Member.Value, 'SetEnvironmentVariable',
                    [StringComparison]::OrdinalIgnoreCase) -and
                @($node.Arguments).Count -eq 3 -and
                $node.Arguments[0] -is [System.Management.Automation.Language.StringConstantExpressionAst] -and
                [string]::Equals(
                    [string]$node.Arguments[0].Value, 'TMPDIR',
                    [StringComparison]::Ordinal) -and
                $node.Arguments[2] -is [System.Management.Automation.Language.MemberExpressionAst] -and
                $node.Arguments[2].Static -and
                [string]::Equals(
                    [string]$node.Arguments[2].Expression.TypeName.FullName, 'EnvironmentVariableTarget',
                    [StringComparison]::OrdinalIgnoreCase) -and
                [string]::Equals(
                    [string]$node.Arguments[2].Member.Value, 'Process',
                    [StringComparison]::OrdinalIgnoreCase)
            }, $true))
    $pinCalls = @($tmpDirectorySetCalls | Where-Object {
            & $isVariable $_.Arguments[1] 'physicalTempRoot'
        })
    $restoreSetCalls = @($tmpDirectorySetCalls | Where-Object {
            & $isVariable $_.Arguments[1] 'tmpDirectoryOriginalValue'
        })
    if ($tmpDirectorySetCalls.Count -ne 2 -or $pinCalls.Count -ne 1 -or
        $restoreSetCalls.Count -ne 1 -or
        $pinCalls[0].Parent -isnot
            [System.Management.Automation.Language.CommandExpressionAst] -or
        $pinCalls[0].Parent.Parent -isnot
            [System.Management.Automation.Language.PipelineAst] -or
        -not [object]::ReferenceEquals(
            $pinCalls[0].Parent.Parent.Parent, $outerTry.Body) -or
        -not (& $isWithin $restoreSetCalls[0] $outerTry.Finally)) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 process-scope TMPDIR pin/restore calls are not exact.'
    }
    $pinStatement = $pinCalls[0].Parent.Parent

    $developmentTopLevelStatements = @($developmentAst.EndBlock.Statements)
    $topLevelFunctions = @($developmentTopLevelStatements | Where-Object {
            $_ -is [System.Management.Automation.Language.FunctionDefinitionAst]
        })
    if ($topLevelFunctions.Count -ne $expectedDevelopmentFunctionNames.Count) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 development top-level function inventory changed.'
    }
    for ($functionIndex = 0;
        $functionIndex -lt $expectedDevelopmentFunctionNames.Count;
        $functionIndex++) {
        if (-not [object]::ReferenceEquals(
                $topLevelFunctions[$functionIndex],
                $developmentFunctionDefinitions[$functionIndex]) -or
            -not [string]::Equals(
                [string]$topLevelFunctions[$functionIndex].Name,
                [string]$expectedDevelopmentFunctionNames[$functionIndex],
                [StringComparison]::OrdinalIgnoreCase)) {
            throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 development top-level function order changed.'
        }
    }
    if (-not ($developmentTopLevelStatements -contains $outerTry)) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 development TMPDIR owner is not top-level.'
    }

    $staticGuardCalls = @($allDevelopmentCommands | Where-Object {
            [string]::Equals(
                [string]$_.GetCommandName(), 'Assert-EdgePluginContractStaticGuard',
                [StringComparison]::OrdinalIgnoreCase)
        })
    $importCommands = @($allDevelopmentCommands | Where-Object {
            [string]::Equals(
                [string]$_.GetCommandName(), 'Import-Module',
                [StringComparison]::OrdinalIgnoreCase)
        })
    $staticGuardImports = @($importCommands | Where-Object {
            $_.Extent.Text -ceq 'Import-Module $staticGuardModulePath -Force'
        })
    $protocolImports = @($importCommands | Where-Object {
            $_.Extent.Text -ceq 'Import-Module $protocolModulePath -Force'
        })
    if ($staticGuardCalls.Count -ne 1 -or
        $staticGuardImports.Count -ne 1 -or
        $protocolImports.Count -ne 1) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 development static/protocol import owner inventory changed.'
    }
    $staticGuardAssignment = $staticGuardCalls[0].Parent
    while ($null -ne $staticGuardAssignment -and
        $staticGuardAssignment -isnot
            [System.Management.Automation.Language.AssignmentStatementAst]) {
        $staticGuardAssignment = $staticGuardAssignment.Parent
    }
    if ($null -eq $staticGuardAssignment -or
        -not (& $isVariable $staticGuardAssignment.Left 'staticGuardResult') -or
        -not [object]::ReferenceEquals(
            $staticGuardAssignment.Parent, $developmentAst.EndBlock) -or
        $staticGuardImports[0].Extent.StartOffset -ge
            $staticGuardAssignment.Extent.StartOffset -or
        $staticGuardAssignment.Extent.StartOffset -ge
            $protocolImports[0].Extent.StartOffset) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 development canonical static owner does not execute before protocol import.'
    }
    $staticResultIfs = @($developmentTopLevelStatements | Where-Object {
            $_ -is [System.Management.Automation.Language.IfStatementAst] -and
            $_.Extent.Text.Contains(
                'EDGE-SPLIT-AUTHORITY-STATIC-002', [StringComparison]::Ordinal)
        })
    if ($staticResultIfs.Count -ne 1 -or
        $staticGuardAssignment.Extent.StartOffset -ge
            $staticResultIfs[0].Extent.StartOffset -or
        $staticResultIfs[0].Extent.StartOffset -ge
            $protocolImports[0].Extent.StartOffset -or
        @($staticResultIfs[0].Clauses).Count -ne 1 -or
        $null -ne $staticResultIfs[0].ElseClause -or
        @($staticResultIfs[0].Clauses[0].Item2.Statements).Count -ne 1 -or
        $staticResultIfs[0].Clauses[0].Item2.Statements[0] -isnot
            [System.Management.Automation.Language.ThrowStatementAst]) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 development static result is not independently asserted before protocol import.'
    }
    $preProtocolForbidden = @($developmentAst.FindAll({
                param($node)
                if ($node.Extent.StartOffset -ge $protocolImports[0].Extent.StartOffset) {
                    return $false
                }
                foreach ($functionDefinition in $developmentFunctionDefinitions) {
                    if (& $isWithin $node $functionDefinition.Body) { return $false }
                }
                return (
                    ($node -is [System.Management.Automation.Language.CommandAst] -and
                        [string]$node.GetCommandName() -in @(
                            'Start-Process', 'New-Item', 'Remove-Item', 'Set-Content',
                            'Add-Content', 'Out-File', 'Invoke-Expression')) -or
                    ($node -is [System.Management.Automation.Language.InvokeMemberExpressionAst] -and
                        [string]$node.Member.Value -in @('Start', 'WriteAllText', 'WriteAllBytes')))
            }, $true))
    if ($preProtocolForbidden.Count -ne 0) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 development performs a child/file side effect before canonical static validation.'
    }

    $pinnedMainProcessCalls = @($invokeDevProcessCalls | Where-Object {
            -not (& $isWithin $_ $invokeDevGitBytesDefinitions[0].Body)
        })
    if (@($outerTry.Body.Statements).Count -eq 0 -or
        -not [object]::ReferenceEquals($pinStatement, $outerTry.Body.Statements[0]) -or
        $pinnedMainProcessCalls.Count -ne 4 -or
        @($pinnedMainProcessCalls | Where-Object {
                -not (& $isWithin $_ $outerTry.Body) -or
                $_.Extent.StartOffset -le $pinStatement.Extent.StartOffset
            }).Count -ne 0) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 TMPDIR pin must be the first outer body statement before every executable child launch.'
    }

    $repositoryRootAssertAssignments = @(& $getVariableAssignments `
        $developmentAst 'RepositoryRoot' | Where-Object {
            if (-not [object]::ReferenceEquals($_.Parent, $outerTry.Body) -or
                $_.Right -isnot [System.Management.Automation.Language.PipelineAst] -or
                @($_.Right.PipelineElements).Count -ne 1 -or
                $_.Right.PipelineElements[0] -isnot
                    [System.Management.Automation.Language.CommandAst]) {
                return $false
            }
            return [string]::Equals(
                [string]$_.Right.PipelineElements[0].GetCommandName(),
                'Assert-DevRepositoryRoot', [StringComparison]::OrdinalIgnoreCase)
        })
    if ($repositoryRootAssertAssignments.Count -ne 1) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 repository root assertion must remain one direct outer TMPDIR body statement.'
    }
    $pinnedSnapshotIfs = @($outerTry.Body.Statements | Where-Object {
            $_ -is [System.Management.Automation.Language.IfStatementAst] -and
            (& $isWithin $snapshotContainsCalls['tmpDirectoryPinnedEnvironment'] $_) -and
            (& $isWithin $snapshotIndexReads['tmpDirectoryPinnedEnvironment'] $_)
        })
    if ($pinnedSnapshotIfs.Count -ne 1 -or
        @($pinnedSnapshotIfs[0].Clauses).Count -ne 1 -or
        $null -ne $pinnedSnapshotIfs[0].ElseClause -or
        @($pinnedSnapshotIfs[0].Clauses[0].Item2.Statements).Count -ne 1 -or
        $pinnedSnapshotIfs[0].Clauses[0].Item2.Statements[0] -isnot
            [System.Management.Automation.Language.ThrowStatementAst] -or
        $environmentAssignments['tmpDirectoryPinnedEnvironment'].Extent.StartOffset -ge
            $pinnedSnapshotIfs[0].Extent.StartOffset -or
        $pinnedSnapshotIfs[0].Extent.StartOffset -ge
            $repositoryRootAssertAssignments[0].Extent.StartOffset) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 pinned TMPDIR snapshot reads escaped their direct verification.'
    }

    $outerRunRootAssignments = @(& $getVariableAssignments $developmentAst 'outerRunRoot')
    if ($outerRunRootAssignments.Count -ne 1 -or
        -not [object]::ReferenceEquals($outerRunRootAssignments[0].Parent, $outerTry.Body)) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 development outer run root assignment is not unique.'
    }
    $outerRootJoinCommands = @($outerRunRootAssignments[0].Right.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.CommandAst] -and
                [string]::Equals(
                    [string]$node.GetCommandName(), 'Join-Path',
                    [StringComparison]::OrdinalIgnoreCase)
            }, $true))
    if ($outerRootJoinCommands.Count -ne 1 -or
        @($outerRootJoinCommands[0].CommandElements | Where-Object {
                & $isVariable $_ 'physicalTempRoot'
            }).Count -ne 1 -or
        $pinStatement.Extent.StartOffset -ge
            $repositoryRootAssertAssignments[0].Extent.StartOffset -or
        $repositoryRootAssertAssignments[0].Extent.StartOffset -ge
            $outerRunRootAssignments[0].Extent.StartOffset) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 development outer run root is not built from the pinned physical root.'
    }

    $mainTries = @($outerTry.Body.Statements | Where-Object {
            $_ -is [System.Management.Automation.Language.TryStatementAst] -and $null -ne $_.Finally
        })
    if ($mainTries.Count -ne 1 -or @($mainTries[0].CatchClauses).Count -ne 1 -or
        -not (& $testDirectFailureCatch $mainTries[0].CatchClauses[0] 'failure')) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 development body lost its cleanup try/finally or first failure catch.'
    }
    $mainTry = $mainTries[0]
    $behaviorDiagnosticStartAssignments = @(& $getVariableAssignments `
        $developmentAst 'behaviorBindingsBase64')
    $behaviorDiagnosticEndAssignments = @(& $getVariableAssignments `
        $developmentAst 'behaviorMarker')
    if ($behaviorDiagnosticStartAssignments.Count -ne 1 -or
        $behaviorDiagnosticEndAssignments.Count -ne 1 -or
        -not (& $isWithin $behaviorDiagnosticStartAssignments[0] $mainTry.Body) -or
        -not (& $isWithin $behaviorDiagnosticEndAssignments[0] $mainTry.Body) -or
        $behaviorDiagnosticStartAssignments[0].Extent.StartOffset -ge
            $behaviorDiagnosticEndAssignments[0].Extent.StartOffset) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 behavior failure diagnostic flow changed.'
    }
    $behaviorDiagnosticStartOffset =
        $behaviorDiagnosticStartAssignments[0].Extent.StartOffset
    $behaviorDiagnosticLength =
        $behaviorDiagnosticEndAssignments[0].Extent.StartOffset -
        $behaviorDiagnosticStartOffset
    $behaviorDiagnosticSource = $DevelopmentSource.Substring(
        $behaviorDiagnosticStartOffset, $behaviorDiagnosticLength)
    $behaviorDiagnosticBytes = [Text.UTF8Encoding]::new($false).GetBytes(
        $behaviorDiagnosticSource)
    $behaviorDiagnosticDigest = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData(
            $behaviorDiagnosticBytes)).ToLowerInvariant()
    if ($behaviorDiagnosticBytes.Length -ne 5245 -or
        $behaviorDiagnosticDigest -cne
            '106bd6c5e541a35f410bbe061e12b4a18320b40215c56a698725f77554c2d72a') {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 behavior failure diagnostic flow changed.'
    }
    $behaviorDiagnosticProcessCalls = @($developmentAst.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.CommandAst] -and
                [string]::Equals(
                    [string]$node.GetCommandName(), 'Invoke-DevProcess',
                    [StringComparison]::OrdinalIgnoreCase) -and
                $node.Extent.StartOffset -ge $behaviorDiagnosticStartOffset -and
                $node.Extent.EndOffset -le
                    ($behaviorDiagnosticStartOffset + $behaviorDiagnosticLength)
            }, $true))
    $behaviorDiagnosticFileWrites = @($developmentAst.FindAll({
                param($node)
                $node -is
                    [System.Management.Automation.Language.InvokeMemberExpressionAst] -and
                $node.Static -and
                $node.Expression -is
                    [System.Management.Automation.Language.TypeExpressionAst] -and
                [string]::Equals(
                    [string]$node.Expression.TypeName.FullName, 'IO.File',
                    [StringComparison]::OrdinalIgnoreCase) -and
                $node.Member -is
                    [System.Management.Automation.Language.StringConstantExpressionAst] -and
                ([string]$node.Member.Value).StartsWith(
                    'Write', [StringComparison]::OrdinalIgnoreCase) -and
                $node.Extent.StartOffset -ge $behaviorDiagnosticStartOffset -and
                $node.Extent.EndOffset -le
                    ($behaviorDiagnosticStartOffset + $behaviorDiagnosticLength)
            }, $true))
    if ($behaviorDiagnosticProcessCalls.Count -ne 1 -or
        $behaviorDiagnosticFileWrites.Count -ne 0) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 behavior failure diagnostic flow changed.'
    }
    $outerTryBytes = [Text.UTF8Encoding]::new($false).GetBytes(
        [string]$outerTry.Extent.Text)
    $outerTryDigest = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData($outerTryBytes)).ToLowerInvariant()
    if (-not $SkipOuterFlowDigest -and
        ($outerTryBytes.Length -ne 21990 -or
        $outerTryDigest -cne '1ea923e8bb08d5180d17b85b1f18233c0d441ea07402ddad21ecace4e957d53c')) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 development post-pin main/cleanup flow changed.'
    }
    $coordinatorCleanupCommands = @($developmentAst.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.CommandAst] -and
                [string]::Equals(
                    [string]$node.GetCommandName(), 'Remove-EdgeDevelopmentCoordinatorRunState',
                    [StringComparison]::OrdinalIgnoreCase)
            }, $true))
    $outerCleanupCommands = @($developmentAst.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.CommandAst] -and
                [string]::Equals(
                    [string]$node.GetCommandName(), 'Remove-EdgeDevelopmentOuterRunRoot',
                    [StringComparison]::OrdinalIgnoreCase)
            }, $true))
    if ($coordinatorCleanupCommands.Count -ne 1 -or $outerCleanupCommands.Count -ne 1 -or
        -not (& $isWithin $coordinatorCleanupCommands[0] $mainTry.Finally) -or
        -not (& $isWithin $outerCleanupCommands[0] $mainTry.Finally) -or
        $coordinatorCleanupCommands[0].Extent.StartOffset -ge $outerCleanupCommands[0].Extent.StartOffset -or
        $outerCleanupCommands[0].Extent.StartOffset -ge $outerTry.Finally.Extent.StartOffset) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 coordinator/outer cleanup invocations lost their exact pre-restore order.'
    }

    $cleanupTries = @($mainTry.Finally.Statements | Where-Object {
            $_ -is [System.Management.Automation.Language.TryStatementAst]
        })
    $coordinatorCleanupTries = @($cleanupTries | Where-Object {
            & $isWithin $coordinatorCleanupCommands[0] $_.Body
        })
    $outerCleanupTries = @($cleanupTries | Where-Object {
            & $isWithin $outerCleanupCommands[0] $_.Body
        })
    if ($cleanupTries.Count -ne 2 -or $coordinatorCleanupTries.Count -ne 1 -or
        $outerCleanupTries.Count -ne 1 -or
        @($coordinatorCleanupTries[0].CatchClauses).Count -ne 1 -or
        @($outerCleanupTries[0].CatchClauses).Count -ne 1 -or
        -not (& $testGuardedFailureCatch $coordinatorCleanupTries[0].CatchClauses[0] 'failure') -or
        -not (& $testGuardedFailureCatch $outerCleanupTries[0].CatchClauses[0] 'failure')) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 cleanup invocations lost their guarded first-failure catches.'
    }
    $coordinatorCleanupBody = @($coordinatorCleanupTries[0].Body.Statements)
    $outerCleanupBody = @($outerCleanupTries[0].Body.Statements)
    if ($coordinatorCleanupBody.Count -ne 1 -or
        $coordinatorCleanupBody[0] -isnot
            [System.Management.Automation.Language.IfStatementAst] -or
        @($coordinatorCleanupBody[0].Clauses).Count -ne 2 -or
        $null -ne $coordinatorCleanupBody[0].ElseClause -or
        [string]$coordinatorCleanupBody[0].Clauses[0].Item1.Extent.Text -cne
            '$null -ne $coordinatorMarker' -or
        @($coordinatorCleanupBody[0].Clauses[0].Item2.Statements).Count -ne 1 -or
        -not [object]::ReferenceEquals(
            $coordinatorCleanupCommands[0].Parent.Parent,
            $coordinatorCleanupBody[0].Clauses[0].Item2) -or
        $outerCleanupBody.Count -ne 1 -or
        $outerCleanupBody[0] -isnot
            [System.Management.Automation.Language.PipelineAst] -or
        -not [object]::ReferenceEquals(
            $outerCleanupCommands[0].Parent, $outerCleanupBody[0]) -or
        -not [object]::ReferenceEquals(
            $outerCleanupBody[0].Parent, $outerCleanupTries[0].Body)) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 cleanup condition/direct call owner changed.'
    }

    $tmpDirectoryRemoveCommands = @($developmentAst.FindAll({
                param($node)
                if ($node -isnot [System.Management.Automation.Language.CommandAst] -or
                    -not [string]::Equals(
                        [string]$node.GetCommandName(), 'Remove-Item',
                        [StringComparison]::OrdinalIgnoreCase)) {
                    return $false
                }
                $values = @($node.CommandElements | Where-Object {
                        $_ -is [System.Management.Automation.Language.StringConstantExpressionAst] -and
                        [string]::Equals(
                            [string]$_.Value, 'Env:TMPDIR',
                            [StringComparison]::OrdinalIgnoreCase)
                    })
                return $values.Count -eq 1
            }, $true))
    if ($tmpDirectoryRemoveCommands.Count -ne 1 -or
        -not (& $isWithin $tmpDirectoryRemoveCommands[0] $outerTry.Finally)) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 absent TMPDIR restoration is not an exact executable removal.'
    }
    $removeParameters = @($tmpDirectoryRemoveCommands[0].CommandElements | Where-Object {
            $_ -is [System.Management.Automation.Language.CommandParameterAst]
        })
    if ($removeParameters.Count -ne 2 -or
        @($removeParameters | Where-Object {
                [string]::Equals($_.ParameterName, 'LiteralPath', [StringComparison]::OrdinalIgnoreCase)
            }).Count -ne 1 -or
        @($removeParameters | Where-Object {
                [string]::Equals($_.ParameterName, 'ErrorAction', [StringComparison]::OrdinalIgnoreCase)
            }).Count -ne 1 -or
        @($tmpDirectoryRemoveCommands[0].CommandElements | Where-Object {
                & $isConstantString $_ 'Stop'
            }).Count -ne 1) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 absent TMPDIR removal parameters are not exact.'
    }
    $tmpDirectoryRemovePathStrings = @(
        $tmpDirectoryRemoveCommands[0].CommandElements | Where-Object {
            $_ -is [System.Management.Automation.Language.StringConstantExpressionAst] -and
            [string]::Equals(
                [string]$_.Value, 'Env:TMPDIR',
                [StringComparison]::OrdinalIgnoreCase)
        })
    $envDriveExecutableStrings = @($executableStrings | Where-Object {
            $null -ne $_.Value -and
            ([string]$_.Value).IndexOf(
                'Env:', [StringComparison]::OrdinalIgnoreCase) -ge 0
        })
    if ($tmpDirectoryRemovePathStrings.Count -ne 1 -or
        $envDriveExecutableStrings.Count -ne 1 -or
        -not [object]::ReferenceEquals(
            $envDriveExecutableStrings[0], $tmpDirectoryRemovePathStrings[0])) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 executable provider paths must contain only the exact TMPDIR restore removal.'
    }

    $tmpRestoreTries = @($outerTry.Finally.Statements | Where-Object {
            $_ -is [System.Management.Automation.Language.TryStatementAst]
        })
    if ($tmpRestoreTries.Count -ne 1 -or @($tmpRestoreTries[0].CatchClauses).Count -ne 1 -or
        -not (& $isWithin $restoreSetCalls[0] $tmpRestoreTries[0].Body) -or
        -not (& $isWithin $tmpDirectoryRemoveCommands[0] $tmpRestoreTries[0].Body) -or
        -not (& $testGuardedFailureCatch $tmpRestoreTries[0].CatchClauses[0] 'tmpDirectoryFailure')) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 TMPDIR restoration lost its guarded first-failure catch.'
    }
    $tmpRestoreIfs = @($tmpRestoreTries[0].Body.Statements | Where-Object {
            $_ -is [System.Management.Automation.Language.IfStatementAst] -and
            @($_.Clauses).Count -eq 1 -and
            (& $testSingleVariableCondition $_.Clauses[0].Item1 'tmpDirectoryWasPresent')
        })
    if ($tmpRestoreIfs.Count -ne 1 -or @($tmpRestoreIfs[0].Clauses).Count -ne 1 -or
        $null -eq $tmpRestoreIfs[0].ElseClause -or
        -not (& $testSingleVariableCondition $tmpRestoreIfs[0].Clauses[0].Item1 'tmpDirectoryWasPresent') -or
        -not (& $isWithin $restoreSetCalls[0] $tmpRestoreIfs[0].Clauses[0].Item2) -or
        -not (& $isWithin $tmpDirectoryRemoveCommands[0] $tmpRestoreIfs[0].ElseClause) -or
        $outerCleanupCommands[0].Extent.StartOffset -ge $restoreSetCalls[0].Extent.StartOffset -or
        $outerCleanupCommands[0].Extent.StartOffset -ge $tmpDirectoryRemoveCommands[0].Extent.StartOffset) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 TMPDIR restore branches/order are not exact.'
    }
    if (-not [object]::ReferenceEquals(
            $tmpRestoreIfs[0].Parent, $tmpRestoreTries[0].Body) -or
        $restoreSetCalls[0].Parent -isnot
            [System.Management.Automation.Language.CommandExpressionAst] -or
        $restoreSetCalls[0].Parent.Parent -isnot
            [System.Management.Automation.Language.PipelineAst] -or
        -not [object]::ReferenceEquals(
            $restoreSetCalls[0].Parent.Parent.Parent,
            $tmpRestoreIfs[0].Clauses[0].Item2) -or
        $tmpDirectoryRemoveCommands[0].Parent -isnot
            [System.Management.Automation.Language.PipelineAst] -or
        -not [object]::ReferenceEquals(
            $tmpDirectoryRemoveCommands[0].Parent.Parent,
            $tmpRestoreIfs[0].ElseClause) -or
        -not [object]::ReferenceEquals(
            $environmentAssignments['tmpDirectoryRestorationEnvironment'].Parent,
            $tmpRestoreTries[0].Body)) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 TMPDIR restore statements are no longer direct canonical branches.'
    }

    $restorationContainsIfs = @($tmpRestoreTries[0].Body.Statements | Where-Object {
            $_ -is [System.Management.Automation.Language.IfStatementAst] -and
            (& $isWithin `
                $snapshotContainsCalls['tmpDirectoryRestorationEnvironment'] $_)
        })
    $restorationIndexIfs = @($tmpRestoreTries[0].Body.Statements | Where-Object {
            $_ -is [System.Management.Automation.Language.IfStatementAst] -and
            (& $isWithin `
                $snapshotIndexReads['tmpDirectoryRestorationEnvironment'] $_)
        })
    foreach ($restorationIf in @($restorationContainsIfs + $restorationIndexIfs)) {
        if (@($restorationIf.Clauses).Count -ne 1 -or
            $null -ne $restorationIf.ElseClause -or
            @($restorationIf.Clauses[0].Item2.Statements).Count -ne 1 -or
            $restorationIf.Clauses[0].Item2.Statements[0] -isnot
                [System.Management.Automation.Language.ThrowStatementAst] -or
            -not [object]::ReferenceEquals(
                $restorationIf.Parent, $tmpRestoreTries[0].Body)) {
            throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 restored TMPDIR snapshot checks must remain direct single-throw guards.'
        }
    }
    if ($restorationContainsIfs.Count -ne 1 -or
        $restorationIndexIfs.Count -ne 1 -or
        [object]::ReferenceEquals(
            $restorationContainsIfs[0], $restorationIndexIfs[0]) -or
        (& $isWithin `
            $snapshotIndexReads['tmpDirectoryRestorationEnvironment'] `
            $restorationContainsIfs[0]) -or
        (& $isWithin `
            $snapshotContainsCalls['tmpDirectoryRestorationEnvironment'] `
            $restorationIndexIfs[0]) -or
        $tmpRestoreIfs[0].Extent.StartOffset -ge
            $environmentAssignments['tmpDirectoryRestorationEnvironment'].Extent.StartOffset -or
        $environmentAssignments['tmpDirectoryRestorationEnvironment'].Extent.StartOffset -ge
            $restorationContainsIfs[0].Extent.StartOffset -or
        $restorationContainsIfs[0].Extent.StartOffset -ge
            $restorationIndexIfs[0].Extent.StartOffset) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 restored TMPDIR snapshot presence/value read order changed.'
    }

    $allCatches = @($developmentAst.FindAll({
                param($node) $node -is [System.Management.Automation.Language.CatchClauseAst]
            }, $true))
    $guardedCurrentDirectoryCatches = @($allCatches | Where-Object {
            & $testGuardedFailureCatch $_ 'currentDirectoryFailure'
        })
    $guardedCleanupCatches = @($allCatches | Where-Object {
            & $testGuardedFailureCatch $_ 'failure'
        })
    $guardedTmpDirectoryCatches = @($allCatches | Where-Object {
            & $testGuardedFailureCatch $_ 'tmpDirectoryFailure'
        })
    if ($guardedCurrentDirectoryCatches.Count -ne 1 -or
        $guardedCleanupCatches.Count -ne 2 -or
        $guardedTmpDirectoryCatches.Count -ne 1) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 guarded first-failure catch count changed.'
    }
    $successStatements = @($developmentTopLevelStatements | Where-Object {
            $_ -is [System.Management.Automation.Language.PipelineAst] -and
            $_.Extent.Text.StartsWith(
                'Write-Output (ConvertTo-EdgeCanonicalJson $developmentValidationResult)',
                [StringComparison]::Ordinal)
        })
    if ($successStatements.Count -ne 1 -or
        -not [object]::ReferenceEquals(
            $successStatements[0], $developmentTopLevelStatements[-1])) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 development structured success owner is not the final top-level statement.'
    }
    $successBytes = [Text.UTF8Encoding]::new($false).GetBytes(
        [string]$successStatements[0].Extent.Text)
    $successDigest = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData($successBytes)).ToLowerInvariant()
    if ($successBytes.Length -ne 71 -or
        $successDigest -cne '363aaa3a789222df3978b7b3ca92ff27b808c1285d960aabb8d2729ac947ec5f') {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 development structured success statement changed.'
    }
}

function Assert-ProtocolModuleImportSurfaceAstGuard {
    param(
        [Parameter(Mandatory)][string]$ProtocolModuleSource,
        [switch]$SkipSourceDigest,
        [switch]$SkipFunctionDigest,
        [switch]$SkipTopLevelDigest,
        [switch]$SkipAddTypeDigest,
        [switch]$SkipCommandOwnerDigest,
        [switch]$SkipMemberOwnerDigest
    )

    $tokens = $null
    $parseErrors = $null
    $moduleAst = [System.Management.Automation.Language.Parser]::ParseInput(
        $ProtocolModuleSource, [ref]$tokens, [ref]$parseErrors)
    if (@($parseErrors).Count -ne 0) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 protocol module source has PowerShell parse errors.'
    }
    $protocolModuleSourceBytes = [Text.UTF8Encoding]::new($false).GetBytes(
        $ProtocolModuleSource)
    $protocolModuleSourceDigest = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData(
            $protocolModuleSourceBytes)).ToLowerInvariant()
    if (-not $SkipSourceDigest -and
        ($protocolModuleSourceBytes.Length -ne 106740 -or
        $protocolModuleSourceDigest -cne '240e39b689a9fa7ea1491154ceebcfd925eb44248c4ad409ae7865562e8709b8')) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 protocol module canonical source bytes changed.'
    }

    $isWithin = {
        param([object]$Node, [object]$Ancestor)
        $current = $Node
        while ($null -ne $current) {
            if ([object]::ReferenceEquals($current, $Ancestor)) { return $true }
            $current = $current.Parent
        }
        return $false
    }
    $getOwningFunction = {
        param([object]$Node)
        $current = $Node.Parent
        while ($null -ne $current) {
            if ($current -is
                [System.Management.Automation.Language.FunctionDefinitionAst]) {
                return $current
            }
            $current = $current.Parent
        }
        return $null
    }
    $matchesVariableName = {
        param([object]$Node, [string]$Name)
        if ($Node -isnot
            [System.Management.Automation.Language.VariableExpressionAst]) {
            return $false
        }
        $userPath = [string]$Node.VariablePath.UserPath
        if ([string]::Equals(
                $userPath, $Name, [StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
        foreach ($scopeName in @('script', 'local', 'global', 'private', 'variable')) {
            if ([string]::Equals(
                    $userPath, "${scopeName}:$Name",
                    [StringComparison]::OrdinalIgnoreCase)) {
                return $true
            }
        }
        return $false
    }
    $isCanonicalVariable = {
        param([object]$Node, [string]$Name)
        return $Node -is
                [System.Management.Automation.Language.VariableExpressionAst] -and
            [string]::Equals(
                [string]$Node.VariablePath.UserPath, $Name,
                [StringComparison]::OrdinalIgnoreCase)
    }

    $expectedFunctionNames = @(
        'Wait-EdgeBoundedCaptureTasks',
        'Get-EdgeSha256Bytes',
        'Get-EdgeSha256Text',
        'Get-EdgeSha256File',
        'Sort-EdgeOrdinalStrings',
        'Test-EdgeByteArrayEqual',
        'Test-EdgeAuthorityCriticalGitEnvironmentName',
        'Assert-EdgeAuthorityNoCriticalGitEnvironment',
        'Assert-EdgeAuthorityGitEnvironment',
        'Assert-EdgeAuthorityEmptyGitConfig',
        'New-EdgeAuthorityGitChildEnvironment',
        'Assert-EdgeAuthorityFinalGitExecutablePath',
        'Get-EdgeAuthorityPinnedPath',
        'Assert-EdgeAuthorityExactPropertyNames',
        'ConvertFrom-EdgeAuthorityCanonicalEnvironmentBinding',
        'New-EdgeAuthorityCoordinatorParentEnvironment',
        'Initialize-EdgeAuthorityCoordinatorParentEnvironment',
        'Assert-EdgeAuthorityCoordinatorParentRequest',
        'Initialize-EdgeAuthorityGitChildEnvironment',
        'Resolve-EdgeFixedExecutable',
        'ConvertFrom-EdgeStrictUtf8Bytes',
        'ConvertFrom-EdgeJsonElement',
        'ConvertFrom-EdgeJsonText',
        'ConvertTo-EdgeCanonicalValue',
        'ConvertTo-EdgeCanonicalJson',
        'ConvertTo-EdgeCanonicalBytes',
        'Assert-EdgeStrictJson',
        'Test-EdgePathIdentity',
        'Resolve-EdgeRepositoryPath',
        'Assert-EdgeExactCanonicalMarker',
        'Assert-EdgeExactTemporaryRunPath',
        'Invoke-EdgeCleanupGit',
        'Remove-EdgeDevelopmentCoordinatorRunState',
        'Remove-EdgeDevelopmentOuterRunRoot',
        'Remove-EdgeFormalAuthorityRunState',
        'Get-EdgeLedgerFactGroupValues',
        'Get-EdgeFactAuthorityKind',
        'Get-EdgeRequiredFactGroupNames',
        'Assert-EdgeFactGroupSet',
        'New-EdgeCandidateFactGroups',
        'New-EdgeAuthorityFactGroups',
        'Assert-EdgeCanonicalEqual',
        'Get-EdgeCountMap',
        'Assert-EdgeCheapLedgerSemantics',
        'Get-EdgeFactErrorCode',
        'Assert-EdgeReceiptFactGroups',
        'Get-EdgeAuthorityCodePaths',
        'Get-EdgeAuthorityCodeDigest',
        'New-EdgeSignedAuthorityReceipt',
        'Read-EdgeLedgerDocument',
        'Read-EdgeGeneratorLedger',
        'Assert-EdgeAuthorityReceipt',
        'Assert-EdgeReplayEquivalent',
        'Assert-EdgeAuthorityDescriptor')
    $moduleStatements = @($moduleAst.EndBlock.Statements)
    $functionDefinitions = @($moduleAst.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.FunctionDefinitionAst]
            }, $true))
    if ($moduleStatements.Count -ne 58 -or
        $functionDefinitions.Count -ne $expectedFunctionNames.Count) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 protocol module top-level/function inventory count changed.'
    }
    for ($functionIndex = 0;
        $functionIndex -lt $expectedFunctionNames.Count;
        $functionIndex++) {
        $functionDefinition = $moduleStatements[3 + $functionIndex]
        if ($functionDefinition -isnot
                [System.Management.Automation.Language.FunctionDefinitionAst] -or
            -not [object]::ReferenceEquals(
                $functionDefinition.Parent, $moduleAst.EndBlock) -or
            $functionDefinition.IsFilter -or $functionDefinition.IsWorkflow -or
            -not [string]::Equals(
                [string]$functionDefinition.Name,
                [string]$expectedFunctionNames[$functionIndex],
                [StringComparison]::OrdinalIgnoreCase) -or
            -not [object]::ReferenceEquals(
                $functionDefinition, $functionDefinitions[$functionIndex])) {
            throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 protocol module function name/order/top-level owner changed.'
        }
    }
    $nonFunctionStatements = @($moduleStatements | Where-Object {
            $_ -isnot [System.Management.Automation.Language.FunctionDefinitionAst]
        })
    if ($nonFunctionStatements.Count -ne 4 -or
        -not [object]::ReferenceEquals($nonFunctionStatements[0], $moduleStatements[0]) -or
        -not [object]::ReferenceEquals($nonFunctionStatements[1], $moduleStatements[1]) -or
        -not [object]::ReferenceEquals($nonFunctionStatements[2], $moduleStatements[2]) -or
        -not [object]::ReferenceEquals($nonFunctionStatements[3], $moduleStatements[57])) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 protocol module executable top-level statement positions changed.'
    }
    $moduleFunctionRows = @($functionDefinitions | ForEach-Object {
            [string]$_.Name + '|' + [string]$_.Extent.Text
        })
    $moduleFunctionText = $moduleFunctionRows -join "`n"
    $moduleFunctionBytes = [Text.UTF8Encoding]::new($false).GetBytes(
        $moduleFunctionText)
    $moduleFunctionDigest = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData(
            $moduleFunctionBytes)).ToLowerInvariant()
    if (-not $SkipFunctionDigest -and
        ($moduleFunctionRows.Count -ne 54 -or
        $moduleFunctionBytes.Length -ne 105805 -or
        $moduleFunctionDigest -cne
            '8782d459dbbf6070f9f9f583b6b1ae75e75f487e7fafb4d81cadee211fb3e315')) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 protocol module canonical function bytes changed.'
    }
    $moduleNonFunctionRows = @()
    foreach ($topLevelIndex in @(0, 1, 2, 57)) {
        $moduleNonFunctionRows += (
            [string]$topLevelIndex + '|' +
            [string]$moduleStatements[$topLevelIndex].Extent.Text)
    }
    $moduleNonFunctionText = $moduleNonFunctionRows -join "`n"
    $moduleNonFunctionBytes = [Text.UTF8Encoding]::new($false).GetBytes(
        $moduleNonFunctionText)
    $moduleNonFunctionDigest = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData(
            $moduleNonFunctionBytes)).ToLowerInvariant()
    if (-not $SkipTopLevelDigest -and
        ($moduleNonFunctionRows.Count -ne 4 -or
        $moduleNonFunctionBytes.Length -ne 2553 -or
        $moduleNonFunctionDigest -cne
            '2ab93cac72e18356e3cb92c9f5c0454766104ffe510fe95e3a4653c98e9f9f8f')) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 protocol module executable top-level statement bytes changed.'
    }

    $setStrictPipeline = $moduleStatements[0]
    $setStrictCommand = if (
        $setStrictPipeline -is [System.Management.Automation.Language.PipelineAst] -and
        @($setStrictPipeline.PipelineElements).Count -eq 1 -and
        $setStrictPipeline.PipelineElements[0] -is
            [System.Management.Automation.Language.CommandAst]) {
        $setStrictPipeline.PipelineElements[0]
    }
    else { $null }
    $setStrictElements = if ($null -ne $setStrictCommand) {
        @($setStrictCommand.CommandElements)
    }
    else { @() }
    if ($null -eq $setStrictCommand -or
        $setStrictCommand.InvocationOperator -ne
            [System.Management.Automation.Language.TokenKind]::Unknown -or
        -not [string]::Equals(
            [string]$setStrictCommand.GetCommandName(), 'Set-StrictMode',
            [StringComparison]::OrdinalIgnoreCase) -or
        $setStrictElements.Count -ne 3 -or
        $setStrictElements[1] -isnot
            [System.Management.Automation.Language.CommandParameterAst] -or
        -not [string]::Equals(
            [string]$setStrictElements[1].ParameterName, 'Version',
            [StringComparison]::OrdinalIgnoreCase) -or
        $setStrictElements[2] -isnot
            [System.Management.Automation.Language.StringConstantExpressionAst] -or
        -not [string]::Equals(
            [string]$setStrictElements[2].Value, 'Latest',
            [StringComparison]::Ordinal)) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 protocol module Set-StrictMode statement changed.'
    }

    $errorActionAssignment = $moduleStatements[1]
    if ($errorActionAssignment -isnot
            [System.Management.Automation.Language.AssignmentStatementAst] -or
        $errorActionAssignment.Operator -ne
            [System.Management.Automation.Language.TokenKind]::Equals -or
        -not (& $isCanonicalVariable `
            $errorActionAssignment.Left 'ErrorActionPreference') -or
        $errorActionAssignment.Right -isnot
            [System.Management.Automation.Language.CommandExpressionAst] -or
        $errorActionAssignment.Right.Expression -isnot
            [System.Management.Automation.Language.StringConstantExpressionAst] -or
        -not [string]::Equals(
            [string]$errorActionAssignment.Right.Expression.Value, 'Stop',
            [StringComparison]::Ordinal)) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 protocol module ErrorActionPreference statement changed.'
    }

    $addTypeIf = $moduleStatements[2]
    if ($addTypeIf -isnot [System.Management.Automation.Language.IfStatementAst] -or
        @($addTypeIf.Clauses).Count -ne 1 -or
        $null -ne $addTypeIf.ElseClause -or
        [string]$addTypeIf.Clauses[0].Item1.Extent.Text -cne
            "-not ('EdgeAuthorityBoundedStreamCapture' -as [type])" -or
        @($addTypeIf.Clauses[0].Item2.Statements).Count -ne 1 -or
        $null -ne $addTypeIf.Clauses[0].Item2.Traps -or
        $addTypeIf.Clauses[0].Item2.Statements[0] -isnot
            [System.Management.Automation.Language.PipelineAst] -or
        @($addTypeIf.Clauses[0].Item2.Statements[0].PipelineElements).Count -ne 1 -or
        $addTypeIf.Clauses[0].Item2.Statements[0].PipelineElements[0] -isnot
            [System.Management.Automation.Language.CommandAst]) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 protocol module Add-Type gate shape changed.'
    }
    $addTypeCommand = $addTypeIf.Clauses[0].Item2.Statements[0].PipelineElements[0]
    $addTypeElements = @($addTypeCommand.CommandElements)
    if ($addTypeCommand.InvocationOperator -ne
            [System.Management.Automation.Language.TokenKind]::Unknown -or
        -not [string]::Equals(
            [string]$addTypeCommand.GetCommandName(), 'Add-Type',
            [StringComparison]::OrdinalIgnoreCase) -or
        $addTypeElements.Count -ne 3 -or
        $addTypeElements[1] -isnot
            [System.Management.Automation.Language.CommandParameterAst] -or
        -not [string]::Equals(
            [string]$addTypeElements[1].ParameterName, 'TypeDefinition',
            [StringComparison]::OrdinalIgnoreCase) -or
        $addTypeElements[2] -isnot
            [System.Management.Automation.Language.StringConstantExpressionAst]) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 protocol module Add-Type command shape changed.'
    }
    $typeDefinitionBytes = [Text.UTF8Encoding]::new($false).GetBytes(
        [string]$addTypeElements[2].Value)
    $typeDefinitionDigest = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData(
            $typeDefinitionBytes)).ToLowerInvariant()
    if (-not $SkipAddTypeDigest -and
        ($typeDefinitionBytes.Length -ne 944 -or
        $typeDefinitionDigest -cne
            'f48f853721752ed2fbbc3182aa0a936d3f9366f0dd816ade79e91dc663076386')) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 protocol module Add-Type TypeDefinition bytes changed.'
    }

    $expectedExportNames = @(
        'Assert-EdgeAuthorityGitEnvironment',
        'Assert-EdgeAuthorityCoordinatorParentRequest',
        'Assert-EdgeAuthorityFinalGitExecutablePath',
        'Assert-EdgeAuthorityEmptyGitConfig',
        'Assert-EdgeAuthorityDescriptor',
        'Assert-EdgeAuthorityReceipt',
        'Assert-EdgeCheapLedgerSemantics',
        'Assert-EdgeFactGroupSet',
        'Assert-EdgeReceiptFactGroups',
        'Assert-EdgeReplayEquivalent',
        'Assert-EdgeStrictJson',
        'ConvertFrom-EdgeJsonText',
        'ConvertTo-EdgeCanonicalBytes',
        'ConvertTo-EdgeCanonicalJson',
        'Get-EdgeAuthorityCodeDigest',
        'Get-EdgeAuthorityCodePaths',
        'Get-EdgeAuthorityPinnedPath',
        'Get-EdgeRequiredFactGroupNames',
        'Get-EdgeLedgerFactGroupValues',
        'Get-EdgeSha256Bytes',
        'Get-EdgeSha256File',
        'Get-EdgeSha256Text',
        'New-EdgeAuthorityFactGroups',
        'New-EdgeCandidateFactGroups',
        'New-EdgeSignedAuthorityReceipt',
        'New-EdgeAuthorityGitChildEnvironment',
        'New-EdgeAuthorityCoordinatorParentEnvironment',
        'Initialize-EdgeAuthorityCoordinatorParentEnvironment',
        'Initialize-EdgeAuthorityGitChildEnvironment',
        'Read-EdgeGeneratorLedger',
        'Remove-EdgeDevelopmentCoordinatorRunState',
        'Remove-EdgeDevelopmentOuterRunRoot',
        'Remove-EdgeFormalAuthorityRunState',
        'Resolve-EdgeFixedExecutable',
        'Resolve-EdgeRepositoryPath',
        'Test-EdgePathIdentity',
        'Wait-EdgeBoundedCaptureTasks')
    $exportPipeline = $moduleStatements[57]
    $exportCommand = if (
        $exportPipeline -is [System.Management.Automation.Language.PipelineAst] -and
        @($exportPipeline.PipelineElements).Count -eq 1 -and
        $exportPipeline.PipelineElements[0] -is
            [System.Management.Automation.Language.CommandAst]) {
        $exportPipeline.PipelineElements[0]
    }
    else { $null }
    $exportElements = if ($null -ne $exportCommand) {
        @($exportCommand.CommandElements)
    }
    else { @() }
    $exportArray = if (
        $exportElements.Count -eq 3 -and
        $exportElements[2] -is
            [System.Management.Automation.Language.ArrayExpressionAst]) {
        $exportElements[2]
    }
    else { $null }
    $exportArrayLiteral = if (
        $null -ne $exportArray -and
        @($exportArray.SubExpression.Statements).Count -eq 1 -and
        $exportArray.SubExpression.Statements[0] -is
            [System.Management.Automation.Language.PipelineAst] -and
        @($exportArray.SubExpression.Statements[0].PipelineElements).Count -eq 1 -and
        $exportArray.SubExpression.Statements[0].PipelineElements[0] -is
            [System.Management.Automation.Language.CommandExpressionAst] -and
        $exportArray.SubExpression.Statements[0].PipelineElements[0].Expression -is
            [System.Management.Automation.Language.ArrayLiteralAst]) {
        $exportArray.SubExpression.Statements[0].PipelineElements[0].Expression
    }
    else { $null }
    if ($null -eq $exportCommand -or
        $exportCommand.InvocationOperator -ne
            [System.Management.Automation.Language.TokenKind]::Unknown -or
        -not [string]::Equals(
            [string]$exportCommand.GetCommandName(), 'Export-ModuleMember',
            [StringComparison]::OrdinalIgnoreCase) -or
        $exportElements.Count -ne 3 -or
        $exportElements[1] -isnot
            [System.Management.Automation.Language.CommandParameterAst] -or
        -not [string]::Equals(
            [string]$exportElements[1].ParameterName, 'Function',
            [StringComparison]::OrdinalIgnoreCase) -or
        $null -eq $exportArrayLiteral -or
        @($exportArrayLiteral.Elements).Count -ne $expectedExportNames.Count) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 protocol module export surface shape changed.'
    }
    for ($exportIndex = 0;
        $exportIndex -lt $expectedExportNames.Count;
        $exportIndex++) {
        $exportElement = $exportArrayLiteral.Elements[$exportIndex]
        if ($exportElement -isnot
                [System.Management.Automation.Language.StringConstantExpressionAst] -or
            -not [string]::Equals(
                [string]$exportElement.Value,
                [string]$expectedExportNames[$exportIndex],
                [StringComparison]::OrdinalIgnoreCase)) {
            throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 protocol module exact Function export set/order changed.'
        }
    }

    $allCommands = @($moduleAst.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.CommandAst]
            }, $true))
    $commandOwnerCounts =
        [Collections.Generic.Dictionary[string, int]]::new(
            [StringComparer]::OrdinalIgnoreCase)
    $topLevelCommands = @()
    foreach ($command in $allCommands) {
        $commandName = [string]$command.GetCommandName()
        if ([string]::IsNullOrWhiteSpace($commandName) -or
            $command.InvocationOperator -ne
                [System.Management.Automation.Language.TokenKind]::Unknown) {
            throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 protocol module contains a dynamic command invocation.'
        }
        $functionOwner = & $getOwningFunction $command
        $ownerName = if ($null -ne $functionOwner) {
            [string]$functionOwner.Name
        }
        else {
            $topLevelCommands += $command
            '<TOP>'
        }
        $key = ($ownerName + '|' + $commandName).ToUpperInvariant()
        $commandOwnerCounts[$key] = 1 + [int]$commandOwnerCounts[$key]
    }
    $commandOwnerRows = [string[]]@($commandOwnerCounts.GetEnumerator() |
        ForEach-Object { [string]$_.Key + '=' + [string]$_.Value })
    [Array]::Sort($commandOwnerRows, [StringComparer]::Ordinal)
    $commandOwnerText = $commandOwnerRows -join "`n"
    $commandOwnerDigest = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData(
            [Text.UTF8Encoding]::new($false).GetBytes(
                $commandOwnerText))).ToLowerInvariant()
    if (-not $SkipCommandOwnerDigest -and
        ($allCommands.Count -ne 323 -or
        $commandOwnerRows.Count -ne 183 -or
        $commandOwnerDigest -cne
            '14715b17665ffb6fbbdc43ad8ef9d55f64df9abe882494bb4fb4cfe890b966b5')) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 protocol module command name/count/owner inventory changed.'
    }
    if ($topLevelCommands.Count -ne 3 -or
        @($topLevelCommands | Where-Object {
                [string]::Equals(
                    [string]$_.GetCommandName(), 'Set-StrictMode',
                    [StringComparison]::OrdinalIgnoreCase)
            }).Count -ne 1 -or
        @($topLevelCommands | Where-Object {
                [string]::Equals(
                    [string]$_.GetCommandName(), 'Add-Type',
                    [StringComparison]::OrdinalIgnoreCase)
            }).Count -ne 1 -or
        @($topLevelCommands | Where-Object {
                [string]::Equals(
                    [string]$_.GetCommandName(), 'Export-ModuleMember',
                    [StringComparison]::OrdinalIgnoreCase)
            }).Count -ne 1) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 protocol module top-level command inventory changed.'
    }

    $allMemberInvocations = @($moduleAst.FindAll({
                param($node)
                $node -is
                    [System.Management.Automation.Language.InvokeMemberExpressionAst]
            }, $true))
    $memberOwnerCounts =
        [Collections.Generic.Dictionary[string, int]]::new(
            [StringComparer]::OrdinalIgnoreCase)
    $topLevelMemberInvocations = @()
    foreach ($memberInvocation in $allMemberInvocations) {
        if ($memberInvocation.Member -isnot
            [System.Management.Automation.Language.StringConstantExpressionAst]) {
            throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 protocol module contains a dynamic member invocation.'
        }
        $functionOwner = & $getOwningFunction $memberInvocation
        $ownerName = if ($null -ne $functionOwner) {
            [string]$functionOwner.Name
        }
        else {
            $topLevelMemberInvocations += $memberInvocation
            '<TOP>'
        }
        $targetIdentity = if ($memberInvocation.Expression -is
            [System.Management.Automation.Language.TypeExpressionAst]) {
            'TYPE:' + [string]$memberInvocation.Expression.TypeName.FullName
        }
        elseif ($memberInvocation.Expression -is
            [System.Management.Automation.Language.VariableExpressionAst]) {
            'VAR:' + [string]$memberInvocation.Expression.VariablePath.UserPath
        }
        else { 'AST:' + $memberInvocation.Expression.GetType().Name }
        $key = ($ownerName + '|' + [string]$memberInvocation.Static + '|' +
            $targetIdentity + '|' +
            [string]$memberInvocation.Member.Value).ToUpperInvariant()
        $memberOwnerCounts[$key] = 1 + [int]$memberOwnerCounts[$key]
    }
    $memberOwnerRows = [string[]]@($memberOwnerCounts.GetEnumerator() |
        ForEach-Object { [string]$_.Key + '=' + [string]$_.Value })
    [Array]::Sort($memberOwnerRows, [StringComparer]::Ordinal)
    $memberOwnerText = $memberOwnerRows -join "`n"
    $memberOwnerDigest = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData(
            [Text.UTF8Encoding]::new($false).GetBytes(
                $memberOwnerText))).ToLowerInvariant()
    if (-not $SkipMemberOwnerDigest -and
        ($allMemberInvocations.Count -ne 264 -or
        $memberOwnerRows.Count -ne 184 -or
        $memberOwnerDigest -cne
            '41a6b1b42adcd7afe036c9048e880d1f3e3e48cdda4fdb61d088e5ae0682aab9' -or
        $topLevelMemberInvocations.Count -ne 0)) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 protocol module member name/count/owner inventory changed.'
    }

    $topLevelAssignments = @($moduleAst.FindAll({
                param($node)
                $node -is
                    [System.Management.Automation.Language.AssignmentStatementAst]
            }, $true) | Where-Object {
            $null -eq (& $getOwningFunction $_)
        })
    if ($topLevelAssignments.Count -ne 1 -or
        -not [object]::ReferenceEquals(
            $topLevelAssignments[0], $errorActionAssignment)) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 protocol module top-level assignment inventory changed.'
    }

    $functionLookup =
        [Collections.Generic.Dictionary[string, object]]::new(
            [StringComparer]::OrdinalIgnoreCase)
    foreach ($functionDefinition in $functionDefinitions) {
        $functionLookup.Add([string]$functionDefinition.Name, $functionDefinition)
    }
    $reachableFunctions =
        [Collections.Generic.HashSet[string]]::new(
            [StringComparer]::OrdinalIgnoreCase)
    $pendingFunctions = [Collections.Generic.Queue[string]]::new()
    foreach ($rootName in @(
            'Assert-EdgeAuthorityGitEnvironment',
            'Resolve-EdgeFixedExecutable',
            'Assert-EdgeAuthorityEmptyGitConfig',
            'New-EdgeAuthorityGitChildEnvironment',
            'Get-EdgeAuthorityPinnedPath')) {
        [void]$pendingFunctions.Enqueue($rootName)
    }
    while ($pendingFunctions.Count -ne 0) {
        $functionName = $pendingFunctions.Dequeue()
        if (-not $reachableFunctions.Add($functionName)) { continue }
        if (-not $functionLookup.ContainsKey($functionName)) {
            throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 protocol module pre-pin root is missing.'
        }
        $functionDefinition = $functionLookup[$functionName]
        $calledCommands = @($functionDefinition.Body.FindAll({
                    param($node)
                    $node -is [System.Management.Automation.Language.CommandAst]
                }, $true))
        foreach ($calledCommand in $calledCommands) {
            $calledName = [string]$calledCommand.GetCommandName()
            if ($functionLookup.ContainsKey($calledName) -and
                -not $reachableFunctions.Contains($calledName)) {
                [void]$pendingFunctions.Enqueue($calledName)
            }
        }
    }
    if ($reachableFunctions.Contains('Invoke-EdgeCleanupGit')) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 protocol module pre-pin import roots can reach the cleanup process owner.'
    }

    $cleanupFunctions = @($functionDefinitions | Where-Object {
            [string]::Equals(
                [string]$_.Name, 'Invoke-EdgeCleanupGit',
                [StringComparison]::OrdinalIgnoreCase)
        })
    if ($cleanupFunctions.Count -ne 1) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 protocol module cleanup process owner is missing or duplicated.'
    }
    $cleanupFunction = $cleanupFunctions[0]
    $startInfoAssignments = @($moduleAst.FindAll({
                param($node)
                $node -is
                    [System.Management.Automation.Language.AssignmentStatementAst] -and
                $node.Left -is
                    [System.Management.Automation.Language.VariableExpressionAst]
            }, $true) | Where-Object {
            & $matchesVariableName $_.Left 'startInfo'
        })
    $processAssignments = @($moduleAst.FindAll({
                param($node)
                $node -is
                    [System.Management.Automation.Language.AssignmentStatementAst] -and
                $node.Left -is
                    [System.Management.Automation.Language.VariableExpressionAst]
            }, $true) | Where-Object {
            & $matchesVariableName $_.Left 'process'
        })
    $startInfoVariables = @($moduleAst.FindAll({
                param($node)
                $node -is
                    [System.Management.Automation.Language.VariableExpressionAst]
            }, $true) | Where-Object {
            & $matchesVariableName $_ 'startInfo'
        })
    $processVariables = @($moduleAst.FindAll({
                param($node)
                $node -is
                    [System.Management.Automation.Language.VariableExpressionAst]
            }, $true) | Where-Object {
            & $matchesVariableName $_ 'process'
        })
    if ($startInfoAssignments.Count -ne 1 -or
        $processAssignments.Count -ne 1 -or
        @($startInfoVariables | Where-Object {
                -not (& $isCanonicalVariable $_ 'startInfo') -or
                -not (& $isWithin $_ $cleanupFunction.Body)
            }).Count -ne 0 -or
        @($processVariables | Where-Object {
                -not (& $isCanonicalVariable $_ 'process') -or
                -not (& $isWithin $_ $cleanupFunction.Body)
            }).Count -ne 0 -or
        -not [object]::ReferenceEquals(
            $startInfoAssignments[0].Parent, $cleanupFunction.Body.EndBlock) -or
        -not [object]::ReferenceEquals(
            $processAssignments[0].Parent, $cleanupFunction.Body.EndBlock)) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 protocol module process variables escaped their unique cleanup owner.'
    }

    $processStartInfoTypes = @($moduleAst.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.TypeExpressionAst] -and
                ([string]::Equals(
                        [string]$node.TypeName.FullName,
                        'Diagnostics.ProcessStartInfo',
                        [StringComparison]::OrdinalIgnoreCase) -or
                    [string]::Equals(
                        [string]$node.TypeName.FullName,
                        'System.Diagnostics.ProcessStartInfo',
                        [StringComparison]::OrdinalIgnoreCase))
            }, $true))
    $processTypes = @($moduleAst.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.TypeExpressionAst] -and
                ([string]::Equals(
                        [string]$node.TypeName.FullName,
                        'Diagnostics.Process',
                        [StringComparison]::OrdinalIgnoreCase) -or
                    [string]::Equals(
                        [string]$node.TypeName.FullName,
                        'System.Diagnostics.Process',
                        [StringComparison]::OrdinalIgnoreCase))
            }, $true))
    $startInfoCreation = if (
        $startInfoAssignments[0].Right -is
            [System.Management.Automation.Language.CommandExpressionAst]) {
        $startInfoAssignments[0].Right.Expression
    }
    else { $null }
    $processCreation = if (
        $processAssignments[0].Right -is
            [System.Management.Automation.Language.CommandExpressionAst]) {
        $processAssignments[0].Right.Expression
    }
    else { $null }
    if ($processStartInfoTypes.Count -ne 1 -or
        $processTypes.Count -ne 1 -or
        $startInfoCreation -isnot
            [System.Management.Automation.Language.InvokeMemberExpressionAst] -or
        $processCreation -isnot
            [System.Management.Automation.Language.InvokeMemberExpressionAst] -or
        -not [object]::ReferenceEquals(
            $processStartInfoTypes[0], $startInfoCreation.Expression) -or
        -not [object]::ReferenceEquals(
            $processTypes[0], $processCreation.Expression) -or
        -not $startInfoCreation.Static -or -not $processCreation.Static -or
        -not [string]::Equals(
            [string]$startInfoCreation.Expression.TypeName.FullName,
            'Diagnostics.ProcessStartInfo',
            [StringComparison]::OrdinalIgnoreCase) -or
        -not [string]::Equals(
            [string]$processCreation.Expression.TypeName.FullName,
            'Diagnostics.Process',
            [StringComparison]::OrdinalIgnoreCase) -or
        $startInfoCreation.Member -isnot
            [System.Management.Automation.Language.StringConstantExpressionAst] -or
        $processCreation.Member -isnot
            [System.Management.Automation.Language.StringConstantExpressionAst] -or
        -not [string]::Equals(
            [string]$startInfoCreation.Member.Value, 'new',
            [StringComparison]::OrdinalIgnoreCase) -or
        -not [string]::Equals(
            [string]$processCreation.Member.Value, 'new',
            [StringComparison]::OrdinalIgnoreCase) -or
        $null -ne $startInfoCreation.Arguments -or
        $null -ne $processCreation.Arguments) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 protocol module Process/ProcessStartInfo construction inventory changed.'
    }

    $startInfoMembers = @($moduleAst.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.MemberExpressionAst] -and
                $node.Member -is
                    [System.Management.Automation.Language.StringConstantExpressionAst] -and
                [string]::Equals(
                    [string]$node.Member.Value, 'StartInfo',
                    [StringComparison]::OrdinalIgnoreCase)
            }, $true))
    $startInfoBindings = @($moduleAst.FindAll({
                param($node)
                $node -is
                    [System.Management.Automation.Language.AssignmentStatementAst] -and
                $node.Left -is
                    [System.Management.Automation.Language.MemberExpressionAst] -and
                $node.Left.Member -is
                    [System.Management.Automation.Language.StringConstantExpressionAst] -and
                [string]::Equals(
                    [string]$node.Left.Member.Value, 'StartInfo',
                    [StringComparison]::OrdinalIgnoreCase)
            }, $true))
    $startInvocations = @($allMemberInvocations | Where-Object {
            [string]::Equals(
                [string]$_.Member.Value, 'Start',
                [StringComparison]::OrdinalIgnoreCase)
        })
    $processOwnerTries = @($cleanupFunction.Body.EndBlock.Statements | Where-Object {
            $_ -is [System.Management.Automation.Language.TryStatementAst] -and
            $null -ne $_.Finally
        })
    if ($startInfoMembers.Count -ne 1 -or
        $startInfoBindings.Count -ne 1 -or
        -not [object]::ReferenceEquals(
            $startInfoMembers[0], $startInfoBindings[0].Left) -or
        $startInfoBindings[0].Left -is
            [System.Management.Automation.Language.InvokeMemberExpressionAst] -or
        -not (& $isCanonicalVariable `
            $startInfoBindings[0].Left.Expression 'process') -or
        $startInfoBindings[0].Right -isnot
            [System.Management.Automation.Language.CommandExpressionAst] -or
        -not (& $isCanonicalVariable `
            $startInfoBindings[0].Right.Expression 'startInfo') -or
        -not [object]::ReferenceEquals(
            $startInfoBindings[0].Parent, $cleanupFunction.Body.EndBlock) -or
        $startInvocations.Count -ne 1 -or $startInvocations[0].Static -or
        -not (& $isCanonicalVariable $startInvocations[0].Expression 'process') -or
        $null -ne $startInvocations[0].Arguments -or
        $processOwnerTries.Count -ne 1 -or
        @($processOwnerTries[0].CatchClauses).Count -ne 0 -or
        -not (& $isWithin $startInvocations[0] $processOwnerTries[0].Body) -or
        $startInfoAssignments[0].Extent.StartOffset -ge
            $processAssignments[0].Extent.StartOffset -or
        $processAssignments[0].Extent.StartOffset -ge
            $startInfoBindings[0].Extent.StartOffset -or
        $startInfoBindings[0].Extent.StartOffset -ge
            $processOwnerTries[0].Extent.StartOffset) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 protocol module cleanup Process owner/start binding changed.'
    }
}

function Assert-EdgePowerShellCanonicalDigestMutationShape {
    param(
        [Parameter(Mandatory)][string]$MutationName,
        [Parameter(Mandatory)][object]$Ast
    )

    $fail = {
        param([string]$Reason)
        throw "EDGE-SPLIT-AUTHORITY-MUTATION-SHAPE-001 '$MutationName' $Reason"
    }
    $isCanonicalVariable = {
        param([object]$Node, [string]$Name)
        return $Node -is [System.Management.Automation.Language.VariableExpressionAst] -and
            [string]::Equals(
                [string]$Node.VariablePath.UserPath, $Name,
                [StringComparison]::OrdinalIgnoreCase)
    }
    $getAssignments = {
        param([string]$Name)
        return @($Ast.FindAll({
                    param($node)
                    $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
                    $node.Left -is [System.Management.Automation.Language.VariableExpressionAst]
                }, $true) | Where-Object {
                & $isCanonicalVariable $_.Left $Name
            })
    }
    $getTopLevelAssignment = {
        param([string]$Name)
        $assignments = @(& $getAssignments $Name)
        if ($assignments.Count -ne 1 -or
            -not [object]::ReferenceEquals($assignments[0].Parent, $Ast.EndBlock)) {
            & $fail "does not have one direct top-level '$Name' assignment."
        }
        return $assignments[0]
    }
    $getRightExpression = {
        param([object]$Assignment)
        if ($Assignment.Right -is
            [System.Management.Automation.Language.CommandExpressionAst]) {
            return $Assignment.Right.Expression
        }
        if ($Assignment.Right -is [System.Management.Automation.Language.PipelineAst] -and
            @($Assignment.Right.PipelineElements).Count -eq 1 -and
            $Assignment.Right.PipelineElements[0] -is
                [System.Management.Automation.Language.CommandExpressionAst]) {
            return $Assignment.Right.PipelineElements[0].Expression
        }
        return $Assignment.Right
    }

    switch ($MutationName) {
        'timeout-executable-default' {
            $owners = @($Ast.FindAll({
                        param($node)
                        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
                        [string]::Equals(
                            [string]$node.Name, 'Invoke-DevProcess',
                            [StringComparison]::OrdinalIgnoreCase)
                    }, $true))
            if ($owners.Count -ne 1 -or
                -not [object]::ReferenceEquals($owners[0].Parent, $Ast.EndBlock)) {
                & $fail 'does not have one top-level Invoke-DevProcess owner.'
            }
            $parameters = @($owners[0].Body.ParamBlock.Parameters | Where-Object {
                    & $isCanonicalVariable $_.Name 'TimeoutSeconds'
                })
            $allParameters = @($Ast.FindAll({
                        param($node)
                        $node -is [System.Management.Automation.Language.ParameterAst] -and
                        $node.Name -is [System.Management.Automation.Language.VariableExpressionAst]
                    }, $true) | Where-Object {
                    & $isCanonicalVariable $_.Name 'TimeoutSeconds'
                })
            $rangeAttributes = @(if ($parameters.Count -eq 1) {
                @($parameters[0].Attributes | Where-Object {
                        $_ -is [System.Management.Automation.Language.AttributeAst] -and
                        [string]::Equals(
                            [string]$_.TypeName.FullName, 'ValidateRange',
                            [StringComparison]::OrdinalIgnoreCase)
                    })
            }
            else { @() })
            $intConstraints = @(if ($parameters.Count -eq 1) {
                @($parameters[0].Attributes | Where-Object {
                        $_ -is [System.Management.Automation.Language.TypeConstraintAst] -and
                        [string]::Equals(
                            [string]$_.TypeName.FullName, 'int',
                            [StringComparison]::OrdinalIgnoreCase)
                    })
            }
            else { @() })
            if ($parameters.Count -ne 1 -or $allParameters.Count -ne 1 -or
                $rangeAttributes.Count -ne 1 -or $intConstraints.Count -ne 1 -or
                @($rangeAttributes[0].PositionalArguments).Count -ne 2 -or
                [string]$rangeAttributes[0].PositionalArguments[0].Extent.Text -cne '1' -or
                [string]$rangeAttributes[0].PositionalArguments[1].Extent.Text -cne '3600' -or
                [string]$rangeAttributes[0].Extent.Text -cne '[ValidateRange(1, 3600)]' -or
                -not [object]::ReferenceEquals(
                    $parameters[0].Parent, $owners[0].Body.ParamBlock)) {
                & $fail 'TimeoutSeconds is not the unique Invoke-DevProcess [ValidateRange(1, 3600)][int] parameter.'
            }
            return
        }
        'strictmode-downgrade' {
            $commands = @($Ast.FindAll({
                        param($node)
                        $node -is [System.Management.Automation.Language.CommandAst] -and
                        [string]::Equals(
                            [string]$node.GetCommandName(), 'Set-StrictMode',
                            [StringComparison]::OrdinalIgnoreCase)
                    }, $true))
            $elements = @(if ($commands.Count -eq 1) {
                @($commands[0].CommandElements)
            }
            else { @() })
            if ($commands.Count -ne 1 -or
                $commands[0].InvocationOperator -ne
                    [System.Management.Automation.Language.TokenKind]::Unknown -or
                $commands[0].Parent -isnot [System.Management.Automation.Language.PipelineAst] -or
                -not [object]::ReferenceEquals(
                    $commands[0].Parent.Parent, $Ast.EndBlock) -or
                $elements.Count -ne 3 -or
                $elements[1] -isnot
                    [System.Management.Automation.Language.CommandParameterAst] -or
                -not [string]::Equals(
                    [string]$elements[1].ParameterName, 'Version',
                    [StringComparison]::OrdinalIgnoreCase) -or
                [string]$elements[2].Extent.Text -cne '3' -or
                [string]$commands[0].Extent.Text -cne 'Set-StrictMode -Version 3') {
                & $fail 'Set-StrictMode -Version 3 is not one direct top-level command.'
            }
            return
        }
        'erroraction-silence' {
            $assignment = & $getTopLevelAssignment 'ErrorActionPreference'
            $expression = & $getRightExpression $assignment
            if ($assignment.Operator -ne
                    [System.Management.Automation.Language.TokenKind]::Equals -or
                $expression -isnot
                    [System.Management.Automation.Language.StringConstantExpressionAst] -or
                [string]$expression.Value -cne 'Continue') {
                & $fail 'ErrorActionPreference is not one direct top-level Continue assignment.'
            }
            return
        }
        'repository-fallback-parent' {
            $fallbackAssignments = @(& $getAssignments 'RepositoryRoot' | Where-Object {
                    @($_.Right.FindAll({
                                param($node)
                                $node -is [System.Management.Automation.Language.CommandAst] -and
                                [string]::Equals(
                                    [string]$node.GetCommandName(), 'Join-Path',
                                    [StringComparison]::OrdinalIgnoreCase)
                            }, $true)).Count -ne 0
                })
            $joinCommands = @(if ($fallbackAssignments.Count -eq 1) {
                @($fallbackAssignments[0].Right.FindAll({
                            param($node)
                            $node -is [System.Management.Automation.Language.CommandAst] -and
                            [string]::Equals(
                                [string]$node.GetCommandName(), 'Join-Path',
                                [StringComparison]::OrdinalIgnoreCase)
                        }, $true))
            }
            else { @() })
            $elements = @(if ($joinCommands.Count -eq 1) {
                @($joinCommands[0].CommandElements)
            }
            else { @() })
            $ifOwner = if ($fallbackAssignments.Count -eq 1) {
                $fallbackAssignments[0].Parent.Parent
            }
            else { $null }
            if ($fallbackAssignments.Count -ne 1 -or $joinCommands.Count -ne 1 -or
                $elements.Count -ne 3 -or
                -not (& $isCanonicalVariable $elements[1] 'PSScriptRoot') -or
                $elements[2] -isnot
                    [System.Management.Automation.Language.StringConstantExpressionAst] -or
                [string]$elements[2].Value -cne '../../..' -or
                $ifOwner -isnot [System.Management.Automation.Language.IfStatementAst] -or
                -not [object]::ReferenceEquals($ifOwner.Parent, $Ast.EndBlock) -or
                @($ifOwner.Clauses).Count -ne 1 -or
                -not [object]::ReferenceEquals(
                    $fallbackAssignments[0].Parent, $ifOwner.Clauses[0].Item2) -or
                [string]$ifOwner.Clauses[0].Item1.Extent.Text -cne
                    '[string]::IsNullOrWhiteSpace($RepositoryRoot)') {
                & $fail 'repository fallback is not the unique top-level If-owned Join-Path ../../.. assignment.'
            }
            return
        }
        'protocol-module-path' {
            $assignment = & $getTopLevelAssignment 'protocolModulePath'
            $commands = @($assignment.Right.FindAll({
                        param($node)
                        $node -is [System.Management.Automation.Language.CommandAst] -and
                        [string]::Equals(
                            [string]$node.GetCommandName(), 'Join-Path',
                            [StringComparison]::OrdinalIgnoreCase)
                    }, $true))
            $elements = @(if ($commands.Count -eq 1) {
                @($commands[0].CommandElements)
            }
            else { @() })
            if ($commands.Count -ne 1 -or $elements.Count -ne 3 -or
                -not (& $isCanonicalVariable $elements[1] 'PSScriptRoot') -or
                $elements[2] -isnot
                    [System.Management.Automation.Language.StringConstantExpressionAst] -or
                [string]$elements[2].Value -cne 'Mutated.Protocol.psm1' -or
                -not [object]::ReferenceEquals($commands[0].Parent, $assignment.Right)) {
                & $fail 'protocolModulePath is not one direct top-level Join-Path Mutated.Protocol.psm1 assignment.'
            }
            return
        }
        'powershell-path-lookup' {
            $assignment = & $getTopLevelAssignment 'powerShellPath'
            $expression = & $getRightExpression $assignment
            if ($expression -isnot
                    [System.Management.Automation.Language.StringConstantExpressionAst] -or
                [string]$expression.Value -cne 'pwsh') {
                & $fail 'powerShellPath is not one direct top-level pwsh string assignment.'
            }
            return
        }
        'git-command-lookup' {
            $assignment = & $getTopLevelAssignment 'gitCommand'
            $commands = @($Ast.FindAll({
                        param($node)
                        $node -is [System.Management.Automation.Language.CommandAst] -and
                        [string]::Equals(
                            [string]$node.GetCommandName(), 'Get-Command',
                            [StringComparison]::OrdinalIgnoreCase)
                    }, $true))
            $ownedCommands = @($assignment.Right.FindAll({
                        param($node)
                        $node -is [System.Management.Automation.Language.CommandAst] -and
                        [string]::Equals(
                            [string]$node.GetCommandName(), 'Get-Command',
                            [StringComparison]::OrdinalIgnoreCase)
                    }, $true))
            $elements = @(if ($ownedCommands.Count -eq 1) {
                @($ownedCommands[0].CommandElements)
            }
            else { @() })
            if ($commands.Count -ne 1 -or $ownedCommands.Count -ne 1 -or
                $elements.Count -ne 6 -or
                [string]$elements[1].Extent.Text -cne 'git' -or
                $elements[2] -isnot
                    [System.Management.Automation.Language.CommandParameterAst] -or
                -not [string]::Equals(
                    [string]$elements[2].ParameterName, 'CommandType',
                    [StringComparison]::OrdinalIgnoreCase) -or
                [string]$elements[3].Extent.Text -cne 'All' -or
                $elements[4] -isnot
                    [System.Management.Automation.Language.CommandParameterAst] -or
                -not [string]::Equals(
                    [string]$elements[4].ParameterName, 'ErrorAction',
                    [StringComparison]::OrdinalIgnoreCase) -or
                [string]$elements[5].Extent.Text -cne 'Stop') {
                & $fail 'gitCommand is not the unique top-level Get-Command git -CommandType All -ErrorAction Stop assignment.'
            }
            return
        }
        'git-path-forge' {
            $assignment = & $getTopLevelAssignment 'gitPath'
            $expression = & $getRightExpression $assignment
            if ($expression -isnot
                    [System.Management.Automation.Language.StringConstantExpressionAst] -or
                [string]$expression.Value -cne 'git') {
                & $fail 'gitPath is not one direct top-level git string assignment.'
            }
            return
        }
        'maximum-output-drift' {
            $assignment = & $getTopLevelAssignment 'devMaximumCapturedBytes'
            $expression = & $getRightExpression $assignment
            if ($expression -isnot
                    [System.Management.Automation.Language.ConstantExpressionAst] -or
                [int64]$expression.Value -ne 1) {
                & $fail 'devMaximumCapturedBytes is not one direct top-level integer 1 assignment.'
            }
            return
        }
        'empty-config-rhs' {
            $assignment = & $getTopLevelAssignment 'devEmptyGitConfigPath'
            $expression = & $getRightExpression $assignment
            if (-not (& $isCanonicalVariable $expression 'null')) {
                & $fail 'devEmptyGitConfigPath is not one direct top-level null assignment.'
            }
            return
        }
        'child-environment-rhs' {
            $assignment = & $getTopLevelAssignment 'devGitChildEnvironment'
            $expression = & $getRightExpression $assignment
            if ($expression -isnot [System.Management.Automation.Language.HashtableAst] -or
                @($expression.KeyValuePairs).Count -ne 0) {
                & $fail 'devGitChildEnvironment is not one direct top-level empty hashtable assignment.'
            }
            return
        }
        'prepin-file-write' {
            $writeInvocations = @($Ast.FindAll({
                        param($node)
                        $node -is
                            [System.Management.Automation.Language.InvokeMemberExpressionAst] -and
                        $node.Static -and
                        $node.Expression -is
                            [System.Management.Automation.Language.TypeExpressionAst] -and
                        [string]::Equals(
                            [string]$node.Expression.TypeName.FullName, 'IO.File',
                            [StringComparison]::OrdinalIgnoreCase) -and
                        $node.Member -is
                            [System.Management.Automation.Language.StringConstantExpressionAst] -and
                        [string]::Equals(
                            [string]$node.Member.Value, 'WriteAllBytes',
                            [StringComparison]::OrdinalIgnoreCase)
                    }, $true))
            $targets = @($writeInvocations | Where-Object {
                    @($_.Arguments).Count -eq 2 -and
                    [string]$_.Arguments[0].Extent.Text -ceq
                        "(Join-Path `$physicalTempRoot 'prepin.bin')" -and
                    $_.Arguments[1] -is
                        [System.Management.Automation.Language.ConvertExpressionAst] -and
                    [string]$_.Arguments[1].Type.TypeName.FullName -ceq 'byte[]' -and
                    [string]$_.Arguments[1].Extent.Text -ceq '[byte[]]@(1)'
                })
            if ($targets.Count -ne 1) {
                & $fail 'prepin.bin is not owned by one real IO.File.WriteAllBytes AST invocation.'
            }
            $joinCommands = @($targets[0].Arguments[0].FindAll({
                        param($node)
                        $node -is [System.Management.Automation.Language.CommandAst] -and
                        [string]::Equals(
                            [string]$node.GetCommandName(), 'Join-Path',
                            [StringComparison]::OrdinalIgnoreCase)
                    }, $true))
            $joinElements = @(if ($joinCommands.Count -eq 1) {
                @($joinCommands[0].CommandElements)
            }
            else { @() })
            $statement = $targets[0]
            while ($null -ne $statement.Parent -and
                -not [object]::ReferenceEquals($statement.Parent, $Ast.EndBlock)) {
                $statement = $statement.Parent
            }
            $pipelineElements = @(if ($statement -is
                    [System.Management.Automation.Language.PipelineAst]) {
                    @($statement.PipelineElements)
                }
                else { @() })
            $directCommandExpressions = @($pipelineElements | Where-Object {
                    $_ -is [System.Management.Automation.Language.CommandExpressionAst]
                })
            $physicalAssignments = @(& $getAssignments 'physicalTempRoot' | Where-Object {
                    [object]::ReferenceEquals($_.Parent, $Ast.EndBlock)
                })
            $pinCalls = @($Ast.FindAll({
                        param($node)
                        $node -is
                            [System.Management.Automation.Language.InvokeMemberExpressionAst] -and
                        $node.Static -and
                        $node.Expression -is
                            [System.Management.Automation.Language.TypeExpressionAst] -and
                        [string]::Equals(
                            [string]$node.Expression.TypeName.FullName, 'Environment',
                            [StringComparison]::OrdinalIgnoreCase) -and
                        $node.Member -is
                            [System.Management.Automation.Language.StringConstantExpressionAst] -and
                        [string]::Equals(
                            [string]$node.Member.Value, 'SetEnvironmentVariable',
                            [StringComparison]::OrdinalIgnoreCase)
                    }, $true) | Where-Object {
                    @($_.Arguments).Count -eq 3 -and
                    $_.Arguments[0] -is
                        [System.Management.Automation.Language.StringConstantExpressionAst] -and
                    [string]$_.Arguments[0].Value -ceq 'TMPDIR' -and
                    (& $isCanonicalVariable $_.Arguments[1] 'physicalTempRoot')
                })
            if ($joinCommands.Count -ne 1 -or $joinElements.Count -ne 3 -or
                -not (& $isCanonicalVariable $joinElements[1] 'physicalTempRoot') -or
                $joinElements[2] -isnot
                    [System.Management.Automation.Language.StringConstantExpressionAst] -or
                [string]$joinElements[2].Value -cne 'prepin.bin' -or
                $statement -isnot [System.Management.Automation.Language.PipelineAst] -or
                -not [object]::ReferenceEquals($statement.Parent, $Ast.EndBlock) -or
                $pipelineElements.Count -ne 1 -or
                $directCommandExpressions.Count -ne 1 -or
                -not [object]::ReferenceEquals(
                    $pipelineElements[0], $directCommandExpressions[0]) -or
                -not [object]::ReferenceEquals(
                    $directCommandExpressions[0].Expression, $targets[0]) -or
                -not [object]::ReferenceEquals(
                    $targets[0].Parent, $directCommandExpressions[0]) -or
                -not [object]::ReferenceEquals(
                    $directCommandExpressions[0].Parent, $statement) -or
                $physicalAssignments.Count -ne 1 -or $pinCalls.Count -ne 1 -or
                $physicalAssignments[0].Extent.StartOffset -ge
                    $targets[0].Extent.StartOffset -or
                $targets[0].Extent.StartOffset -ge $pinCalls[0].Extent.StartOffset) {
                & $fail 'File.WriteAllBytes is not the unique direct top-level statement between physical-temp assignment and TMPDIR pin.'
            }
            return
        }
        default {
            & $fail 'is not a declared PowerShell CanonicalDigest mutation.'
        }
    }
}

function Assert-EdgeAddTypeCanonicalDigestMutationShape {
    param(
        [Parameter(Mandatory)][string]$MutationName,
        [Parameter(Mandatory)][object]$PowerShellAst
    )

    $fail = {
        param([string]$Reason)
        throw "EDGE-SPLIT-AUTHORITY-MUTATION-SHAPE-001 '$MutationName' $Reason"
    }
    $addTypeCommands = @($PowerShellAst.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.CommandAst] -and
                [string]::Equals(
                    [string]$node.GetCommandName(), 'Add-Type',
                    [StringComparison]::OrdinalIgnoreCase)
            }, $true))
    if ($addTypeCommands.Count -ne 1) {
        & $fail 'does not have one Add-Type owner.'
    }
    $elements = @($addTypeCommands[0].CommandElements)
    $typeDefinitionParameters = @($elements | Where-Object {
            $_ -is [System.Management.Automation.Language.CommandParameterAst] -and
            [string]::Equals(
                [string]$_.ParameterName, 'TypeDefinition',
                [StringComparison]::OrdinalIgnoreCase)
        })
    if ($typeDefinitionParameters.Count -ne 1) {
        & $fail 'does not have one Add-Type TypeDefinition parameter.'
    }
    $parameterIndex = [Array]::IndexOf($elements, $typeDefinitionParameters[0])
    if ($parameterIndex -lt 0 -or $parameterIndex + 1 -ge $elements.Count -or
        $elements[$parameterIndex + 1] -isnot
            [System.Management.Automation.Language.StringConstantExpressionAst]) {
        & $fail 'TypeDefinition is not one literal PowerShell AST value.'
    }
    $csharpSource = [string]$elements[$parameterIndex + 1].Value
    if ($null -eq ('Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree' -as [type])) {
        foreach ($assemblyName in @(
                'Microsoft.CodeAnalysis.dll',
                'Microsoft.CodeAnalysis.CSharp.dll')) {
            $assemblyPath = Join-Path $PSHOME $assemblyName
            if (-not [IO.File]::Exists($assemblyPath)) {
                & $fail "Roslyn assembly '$assemblyName' is unavailable."
            }
            [void][Reflection.Assembly]::LoadFrom($assemblyPath)
        }
    }
    $tree = [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText($csharpSource)
    if (@($tree.GetDiagnostics() | Where-Object {
                $_.Severity -eq [Microsoft.CodeAnalysis.DiagnosticSeverity]::Error
            }).Count -ne 0) {
        & $fail 'TypeDefinition C# has syntax errors.'
    }
    $root = $tree.GetRoot()
    $classes = @($root.DescendantNodes() | Where-Object {
            $_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax] -and
            [string]$_.Identifier.ValueText -ceq 'EdgeAuthorityBoundedStreamCapture'
        })
    if ($classes.Count -ne 1) {
        & $fail 'does not have one target C# class declaration.'
    }
    $class = $classes[0]
    $testInvocation = {
        param(
            [object]$Invocation,
            [string]$Receiver,
            [string]$Member,
            [string[]]$LiteralArguments
        )
        if ($Invocation -isnot
                [Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax] -or
            $Invocation.Expression -isnot
                [Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax] -or
            [string]$Invocation.Expression.Expression.ToString() -cne $Receiver -or
            [string]$Invocation.Expression.Name.Identifier.ValueText -cne $Member) {
            return $false
        }
        $arguments = @($Invocation.ArgumentList.Arguments)
        if ($arguments.Count -ne $LiteralArguments.Count) { return $false }
        for ($index = 0; $index -lt $LiteralArguments.Count; $index++) {
            if ($arguments[$index].Expression -isnot
                    [Microsoft.CodeAnalysis.CSharp.Syntax.LiteralExpressionSyntax] -or
                [string]$arguments[$index].Expression.Token.ValueText -cne
                    [string]$LiteralArguments[$index]) {
                return $false
            }
        }
        return $true
    }

    if ($MutationName -ceq 'module-addtype-process-injection') {
        $methods = @($class.Members | Where-Object {
                $_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax] -and
                [string]$_.Identifier.ValueText -ceq 'StartInjectedProcess'
            })
        $invocations = @(if ($methods.Count -eq 1 -and $null -ne $methods[0].Body) {
            @($methods[0].Body.DescendantNodes() | Where-Object {
                    $_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax]
                })
        }
        else { @() })
        if ($methods.Count -ne 1 -or $invocations.Count -ne 1 -or
            -not (& $testInvocation $invocations[0] `
                'System.Diagnostics.Process' 'Start' @('true'))) {
            & $fail 'does not have one real StartInjectedProcess method body calling System.Diagnostics.Process.Start("true").'
        }
        return
    }
    if ($MutationName -ceq 'module-addtype-static-constructor') {
        $constructors = @($class.Members | Where-Object {
                $_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.ConstructorDeclarationSyntax] -and
                [string]$_.Identifier.ValueText -ceq
                    'EdgeAuthorityBoundedStreamCapture' -and
                @($_.Modifiers | Where-Object {
                        [string]$_.ValueText -ceq 'static'
                    }).Count -eq 1
            })
        $invocations = @(if ($constructors.Count -eq 1 -and
            $null -ne $constructors[0].Body) {
            @($constructors[0].Body.DescendantNodes() | Where-Object {
                    $_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax]
                })
        }
        else { @() })
        if ($constructors.Count -ne 1 -or
            @($constructors[0].ParameterList.Parameters).Count -ne 0 -or
            $invocations.Count -ne 1 -or
            -not (& $testInvocation $invocations[0] `
                'Environment' 'SetEnvironmentVariable' `
                @('EDGE_AUTHORITY_STATIC_MUTATION', '1'))) {
            & $fail 'does not have one real target-class static constructor with the expected environment call.'
        }
        return
    }
    if ($MutationName -ceq 'module-addtype-file-write-injection') {
        $methods = @($class.Members | Where-Object {
                $_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax] -and
                [string]$_.Identifier.ValueText -ceq 'WriteInjectedFile'
            })
        $invocations = @(if ($methods.Count -eq 1 -and $null -ne $methods[0].Body) {
            @($methods[0].Body.DescendantNodes() | Where-Object {
                    $_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax]
                })
        }
        else { @() })
        $writeCall = @($invocations | Where-Object {
                $_.Expression -is
                    [Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax] -and
                [string]$_.Expression.Expression.ToString() -ceq 'System.IO.File' -and
                [string]$_.Expression.Name.Identifier.ValueText -ceq 'WriteAllText'
            })
        $pathCall = if ($writeCall.Count -eq 1 -and
            @($writeCall[0].ArgumentList.Arguments).Count -eq 2) {
            $writeCall[0].ArgumentList.Arguments[0].Expression
        }
        else { $null }
        $edgeLiteral = if ($writeCall.Count -eq 1 -and
            @($writeCall[0].ArgumentList.Arguments).Count -eq 2) {
            $writeCall[0].ArgumentList.Arguments[1].Expression
        }
        else { $null }
        if ($methods.Count -ne 1 -or $writeCall.Count -ne 1 -or
            $pathCall -isnot
                [Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax] -or
            $pathCall.Expression -isnot
                [Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax] -or
            [string]$pathCall.Expression.Expression.ToString() -cne 'System.IO.Path' -or
            [string]$pathCall.Expression.Name.Identifier.ValueText -cne
                'GetTempFileName' -or
            @($pathCall.ArgumentList.Arguments).Count -ne 0 -or
            $edgeLiteral -isnot
                [Microsoft.CodeAnalysis.CSharp.Syntax.LiteralExpressionSyntax] -or
            [string]$edgeLiteral.Token.ValueText -cne 'edge') {
            & $fail 'does not have one real WriteInjectedFile method body calling System.IO.File.WriteAllText with a real GetTempFileName invocation.'
        }
        return
    }
    & $fail 'is not a declared Add-Type CanonicalDigest mutation.'
}

function Assert-EdgeStaticMutationShape {
    param(
        [Parameter(Mandatory)][ValidateSet('development', 'protocolModule')]
        [string]$Target,
        [Parameter(Mandatory)][string]$MutationName,
        [Parameter(Mandatory)][string]$ExpectedShape,
        [Parameter(Mandatory)][string]$Source
    )

    $shapeNames = [ordered]@{
        'timeout-executable-default' = 'Development.InvokeDevProcessTimeoutParameter'
        'strictmode-downgrade' = 'Development.StrictModeTopLevel'
        'erroraction-silence' = 'Development.ErrorActionTopLevel'
        'repository-fallback-parent' = 'Development.RepositoryFallback'
        'protocol-module-path' = 'Development.ProtocolModulePath'
        'powershell-path-lookup' = 'Development.PowerShellPathBinding'
        'git-command-lookup' = 'Development.GitCommandBinding'
        'git-path-forge' = 'Development.GitPathBinding'
        'maximum-output-drift' = 'Development.MaximumOutputBinding'
        'empty-config-rhs' = 'Development.EmptyGitConfigBinding'
        'child-environment-rhs' = 'Development.GitChildEnvironmentBinding'
        'prepin-start-process' = 'Development.PrePinStatement'
        'prepin-static-process-start' = 'Development.PrePinStatement'
        'prepin-file-write' = 'Development.PrePinStatement'
        'prepin-native-command' = 'Development.PrePinStatement'
        'prepin-new-object-process' = 'Development.PrePinStatement'
        'prepin-invoke-expression' = 'Development.PrePinStatement'
        'prepin-set-alias' = 'Development.PrePinStatement'
        'prepin-env-provider-write' = 'Development.PrePinStatement'
        'prepin-direct-child' = 'Development.PrePinStatement'
        'shadow-remove-item' = 'Development.FunctionShadow'
        'shadow-get-item' = 'Development.FunctionShadow'
        'shadow-invokedev-case' = 'Development.FunctionShadow'
        'nested-shadow-get-item' = 'Development.FunctionShadow'
        'process-scoped-alias' = 'Development.ProcessScopedVariableAlias'
        'process-ref-escape' = 'Development.ProcessReferenceEscape'
        'helper-process-start' = 'Development.PhysicalTempHelperProcess'
        'physical-temp-scoped-alias' = 'Development.PhysicalTempScopedVariableAlias'
        'physical-temp-ref-escape' = 'Development.PhysicalTempReferenceEscape'
        'remove-coordinator-cleanup' = 'Development.CoordinatorCleanupRemoved'
        'dev-remove-coordinator-cleanup' = 'Development.CoordinatorCleanupUnreachable'
        'remove-outer-cleanup' = 'Development.OuterCleanupRemoved'
        'dev-remove-outer-cleanup' = 'Development.OuterCleanupUnreachable'
        'absent-restore-alias' = 'Development.RestoreCommandAlias'
        'post-pin-early-success-exit' = 'Development.PostPinEarlySuccessExit'
        'module-top-start-process' = 'Protocol.TopLevelStatement'
        'module-top-static-process-start' = 'Protocol.TopLevelStatement'
        'module-top-file-write' = 'Protocol.TopLevelStatement'
        'module-function-shadow-get-item' = 'Protocol.FunctionShadow'
        'module-function-shadow-remove-item' = 'Protocol.FunctionShadow'
        'module-addtype-process-injection' = 'Protocol.AddTypeProcessBody'
        'module-addtype-static-constructor' = 'Protocol.AddTypeStaticConstructor'
        'module-addtype-file-write-injection' = 'Protocol.AddTypeFileWriteBody'
        'module-top-pure-expression' = 'Protocol.TopLevelStatement'
    }
    $requiredShape = if ($shapeNames.Contains($MutationName)) {
        [string]$shapeNames[$MutationName]
    }
    else { 'None' }
    if ($ExpectedShape -cne $requiredShape) {
        throw "EDGE-SPLIT-AUTHORITY-MUTATION-TARGET-001 '$MutationName' shape '$ExpectedShape' differs from canonical '$requiredShape'."
    }
    if ($requiredShape -ceq 'None') { return }

    $tokens = $null
    $parseErrors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseInput(
        $Source, [ref]$tokens, [ref]$parseErrors)
    if (@($parseErrors).Count -ne 0) {
        throw "EDGE-SPLIT-AUTHORITY-MUTATION-TARGET-001 '$MutationName' shape source has parse errors."
    }
    $powerShellCanonicalDigestMutations = @(
        'timeout-executable-default',
        'strictmode-downgrade',
        'erroraction-silence',
        'repository-fallback-parent',
        'protocol-module-path',
        'powershell-path-lookup',
        'git-command-lookup',
        'git-path-forge',
        'maximum-output-drift',
        'empty-config-rhs',
        'child-environment-rhs',
        'prepin-file-write')
    $addTypeCanonicalDigestMutations = @(
        'module-addtype-process-injection',
        'module-addtype-static-constructor',
        'module-addtype-file-write-injection')
    if ($powerShellCanonicalDigestMutations -contains $MutationName) {
        if ($Target -cne 'development') {
            throw "EDGE-SPLIT-AUTHORITY-MUTATION-SHAPE-001 '$MutationName' is not owned by Development."
        }
        Assert-EdgePowerShellCanonicalDigestMutationShape $MutationName $ast
        return
    }
    if ($addTypeCanonicalDigestMutations -contains $MutationName) {
        if ($Target -cne 'protocolModule') {
            throw "EDGE-SPLIT-AUTHORITY-MUTATION-SHAPE-001 '$MutationName' is not owned by ProtocolModule."
        }
        Assert-EdgeAddTypeCanonicalDigestMutationShape $MutationName $ast
        return
    }
    $needleByName = [ordered]@{
        'timeout-executable-default' = '[ValidateRange(1, 3600)][int]$TimeoutSeconds,'
        'strictmode-downgrade' = 'Set-StrictMode -Version 3'
        'erroraction-silence' = '$ErrorActionPreference = ''Continue'''
        'repository-fallback-parent' = "Join-Path `$PSScriptRoot '../../..'"
        'protocol-module-path' = "'Mutated.Protocol.psm1'"
        'powershell-path-lookup' = '$powerShellPath = ''pwsh'''
        'git-command-lookup' = 'Get-Command git -CommandType All -ErrorAction Stop'
        'git-path-forge' = '$gitPath = ''git'''
        'maximum-output-drift' = '$devMaximumCapturedBytes = 1'
        'empty-config-rhs' = '$devEmptyGitConfigPath = $null'
        'child-environment-rhs' = '$devGitChildEnvironment = @{}'
        'prepin-start-process' = 'Start-Process -FilePath $powerShellPath'
        'prepin-static-process-start' = '$null = [Diagnostics.Process]::Start($powerShellPath)'
        'prepin-file-write' = '[IO.File]::WriteAllBytes((Join-Path $physicalTempRoot ''prepin.bin''), [byte[]]@(1))'
        'prepin-native-command' = '& $gitPath --version'
        'prepin-new-object-process' = '$null = New-Object Diagnostics.Process'
        'prepin-invoke-expression' = 'Invoke-Expression ''Get-Date'''
        'prepin-set-alias' = 'Set-Alias -Name edgePrePinMutation -Value Get-Item'
        'prepin-env-provider-write' = '$Env:TMPDIR = $physicalTempRoot'
        'prepin-direct-child' = '$null = Invoke-DevProcess -FileName $powerShellPath'
        'shadow-remove-item' = 'function Remove-Item { throw ''mutated remove owner'' }'
        'shadow-get-item' = 'function Get-Item { throw ''mutated get owner'' }'
        'shadow-invokedev-case' = 'function invoke-devprocess { throw ''mutated process owner'' }'
        'nested-shadow-get-item' = 'function Get-Item { throw ''mutated nested get owner'' }'
        'helper-process-start' = '$null = [Diagnostics.Process]::Start($powerShellPath)'
        'absent-restore-alias' = "ri -LiteralPath 'Env:TMPDIR' -ErrorAction Stop"
        'post-pin-early-success-exit' = 'Write-Output ''{"schemaVersion":1,"passed":true}''; exit 0'
        'module-top-start-process' = 'Start-Process -FilePath ''true'''
        'module-top-static-process-start' = '$null = [Diagnostics.Process]::Start(''true'')'
        'module-top-file-write' = '[IO.File]::WriteAllText(''/tmp/edge-static-mutation'', ''x'')'
        'module-function-shadow-get-item' = 'function Get-Item { throw ''mutated'' }'
        'module-function-shadow-remove-item' = 'function Remove-Item { throw ''mutated'' }'
        'module-addtype-process-injection' = '_ = System.Diagnostics.Process.Start("true");'
        'module-addtype-static-constructor' = 'static EdgeAuthorityBoundedStreamCapture()'
        'module-addtype-file-write-injection' = 'System.IO.File.WriteAllText(System.IO.Path.GetTempFileName(), "edge");'
        'module-top-pure-expression' = '1 + 1'
    }
    if ($needleByName.Contains($MutationName) -and
        -not $Source.Contains(
            [string]$needleByName[$MutationName], [StringComparison]::Ordinal)) {
        throw "EDGE-SPLIT-AUTHORITY-MUTATION-TARGET-001 '$MutationName' required shape needle is absent."
    }

    if ($requiredShape -ceq 'Development.PrePinStatement') {
        $anchorOffset = $Source.IndexOf(
            '$physicalTempRoot = Resolve-DevPhysicalTempRoot',
            [StringComparison]::Ordinal)
        $shapeOffset = $Source.IndexOf(
            [string]$needleByName[$MutationName], [StringComparison]::Ordinal)
        $pinOffset = $Source.IndexOf(
            "[Environment]::SetEnvironmentVariable(`n        'TMPDIR'",
            [StringComparison]::Ordinal)
        if ($anchorOffset -lt 0 -or $shapeOffset -le $anchorOffset -or
            $pinOffset -le $shapeOffset) {
            throw "EDGE-SPLIT-AUTHORITY-MUTATION-TARGET-001 '$MutationName' is not owned by the pre-pin region."
        }
    }
    elseif ($requiredShape -ceq 'Development.FunctionShadow' -or
        $requiredShape -ceq 'Protocol.FunctionShadow') {
        $shadowName = if ($MutationName.Contains('remove', [StringComparison]::Ordinal)) {
            'Remove-Item'
        }
        elseif ($MutationName.Contains('invokedev', [StringComparison]::Ordinal)) {
            'invoke-devprocess'
        }
        else { 'Get-Item' }
        $owners = @($ast.FindAll({
                    param($node)
                    $node -is [System.Management.Automation.Language.FunctionDefinitionAst]
                }, $true) | Where-Object {
                [string]::Equals(
                    [string]$_.Name, $shadowName,
                    [StringComparison]::OrdinalIgnoreCase)
            })
        $expectedOwnerCount = if ($MutationName -ceq 'shadow-invokedev-case') {
            2
        }
        else { 1 }
        if ($owners.Count -ne $expectedOwnerCount) {
            throw "EDGE-SPLIT-AUTHORITY-MUTATION-TARGET-001 '$MutationName' function shadow shape is absent or duplicated."
        }
    }
    elseif ($requiredShape -in @(
            'Development.ProcessScopedVariableAlias',
            'Development.PhysicalTempScopedVariableAlias')) {
        $variableName = if ($MutationName -ceq 'process-scoped-alias') {
            'script:process'
        }
        else { 'script:physicalTempRoot' }
        $assignments = @($ast.FindAll({
                    param($node)
                    $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
                    $node.Left -is [System.Management.Automation.Language.VariableExpressionAst]
                }, $true) | Where-Object {
                [string]::Equals(
                    [string]$_.Left.VariablePath.UserPath, $variableName,
                    [StringComparison]::OrdinalIgnoreCase)
            })
        if ($assignments.Count -ne 1) {
            throw "EDGE-SPLIT-AUTHORITY-MUTATION-TARGET-001 '$MutationName' scoped protected-variable assignment shape is absent."
        }
    }
    elseif ($requiredShape -in @(
            'Development.ProcessReferenceEscape',
            'Development.PhysicalTempReferenceEscape')) {
        $variableName = if ($MutationName -ceq 'process-ref-escape') {
            'process'
        }
        else { 'physicalTempRoot' }
        $references = @($ast.FindAll({
                    param($node)
                    $node -is [System.Management.Automation.Language.ConvertExpressionAst] -and
                    [string]::Equals(
                        [string]$node.Type.TypeName.FullName, 'ref',
                        [StringComparison]::OrdinalIgnoreCase) -and
                    $node.Child -is [System.Management.Automation.Language.VariableExpressionAst]
                }, $true) | Where-Object {
                [string]::Equals(
                    [string]$_.Child.VariablePath.UserPath, $variableName,
                    [StringComparison]::OrdinalIgnoreCase)
            })
        if ($references.Count -ne 1) {
            throw "EDGE-SPLIT-AUTHORITY-MUTATION-TARGET-001 '$MutationName' protected-variable reference escape shape is absent."
        }
    }
    elseif ($requiredShape -in @(
            'Development.CoordinatorCleanupRemoved',
            'Development.CoordinatorCleanupUnreachable',
            'Development.OuterCleanupRemoved',
            'Development.OuterCleanupUnreachable')) {
        $commandName = if ($MutationName.Contains(
                'coordinator', [StringComparison]::Ordinal)) {
            'Remove-EdgeDevelopmentCoordinatorRunState'
        }
        else { 'Remove-EdgeDevelopmentOuterRunRoot' }
        $commands = @($ast.FindAll({
                    param($node)
                    $node -is [System.Management.Automation.Language.CommandAst]
                }, $true) | Where-Object {
                [string]::Equals(
                    [string]$_.GetCommandName(), $commandName,
                    [StringComparison]::OrdinalIgnoreCase)
            })
        if ($requiredShape.EndsWith('Removed', [StringComparison]::Ordinal)) {
            if ($commands.Count -ne 0) {
                throw "EDGE-SPLIT-AUTHORITY-MUTATION-TARGET-001 '$MutationName' did not remove its cleanup call."
            }
        }
        else {
            if ($commands.Count -ne 1) {
                throw "EDGE-SPLIT-AUTHORITY-MUTATION-TARGET-001 '$MutationName' unreachable cleanup call count changed."
            }
            $ancestor = $commands[0].Parent
            while ($null -ne $ancestor -and
                $ancestor -isnot [System.Management.Automation.Language.IfStatementAst]) {
                $ancestor = $ancestor.Parent
            }
            if ($ancestor -isnot [System.Management.Automation.Language.IfStatementAst] -or
                @($ancestor.Clauses).Count -ne 1 -or
                -not ([string]$ancestor.Clauses[0].Item1.Extent.Text).Contains(
                    '$false', [StringComparison]::Ordinal)) {
                throw "EDGE-SPLIT-AUTHORITY-MUTATION-TARGET-001 '$MutationName' cleanup call is not in an unreachable branch."
            }
        }
    }
    elseif ($requiredShape -ceq 'Development.PhysicalTempHelperProcess') {
        $helper = @($ast.FindAll({
                    param($node)
                    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
                    [string]::Equals(
                        [string]$node.Name, 'Resolve-DevPhysicalTempRoot',
                        [StringComparison]::OrdinalIgnoreCase)
                }, $true))
        if ($helper.Count -ne 1 -or
            -not ([string]$helper[0].Extent.Text).Contains(
                [string]$needleByName[$MutationName], [StringComparison]::Ordinal)) {
            throw "EDGE-SPLIT-AUTHORITY-MUTATION-TARGET-001 '$MutationName' process call is not owned by the physical-temp helper."
        }
    }
    elseif ($requiredShape -ceq 'Development.PostPinEarlySuccessExit') {
        $shapeOffset = $Source.IndexOf(
            [string]$needleByName[$MutationName], [StringComparison]::Ordinal)
        $pinOffset = $Source.IndexOf(
            "[Environment]::SetEnvironmentVariable(`n        'TMPDIR'",
            [StringComparison]::Ordinal)
        if ($pinOffset -lt 0 -or $shapeOffset -le $pinOffset) {
            throw "EDGE-SPLIT-AUTHORITY-MUTATION-TARGET-001 '$MutationName' success exit is not in the post-pin region."
        }
    }
    elseif ($requiredShape -ceq 'Protocol.TopLevelStatement') {
        $needle = [string]$needleByName[$MutationName]
        $nodes = @($ast.EndBlock.Statements | Where-Object {
                ([string]$_.Extent.Text).Contains(
                    $needle, [StringComparison]::Ordinal)
            })
        if ($nodes.Count -ne 1) {
            throw "EDGE-SPLIT-AUTHORITY-MUTATION-TARGET-001 '$MutationName' statement is not a unique protocol top-level owner."
        }
    }
}

function Get-EdgeStaticMutationFailureOwner {
    param([Parameter(Mandatory)][string]$Message)
    switch ($Message) {
        'EDGE-SPLIT-AUTHORITY-STATIC-001 development canonical source bytes changed.' { return 'Development.CanonicalSourceDigest' }
        'EDGE-SPLIT-AUTHORITY-STATIC-001 development CmdletBinding contract changed before TMPDIR pin.' { return 'Development.CmdletBinding' }
        'EDGE-SPLIT-AUTHORITY-STATIC-001 development parameter contract changed before TMPDIR pin.' { return 'Development.ParameterContract' }
        'EDGE-SPLIT-AUTHORITY-STATIC-001 development function inventory count changed.' { return 'Development.FunctionInventory' }
        'EDGE-SPLIT-AUTHORITY-STATIC-001 development canonical function bytes changed.' { return 'Development.CanonicalFunctionDigest' }
        'EDGE-SPLIT-AUTHORITY-STATIC-001 development dirty manifest canonical byte-array shape changed.' { return 'Development.ManifestCanonicalBytes' }
        'EDGE-SPLIT-AUTHORITY-STATIC-001 Invoke-DevProcess call ownership/count changed.' { return 'Development.ProcessCallOwnership' }
        'EDGE-SPLIT-AUTHORITY-STATIC-001 Invoke-DevProcess must create exactly one direct ProcessStartInfo owner.' { return 'Development.ProcessStartInfoOwner' }
        'EDGE-SPLIT-AUTHORITY-STATIC-001 Invoke-DevProcess Process owner/start binding inventory changed.' { return 'Development.ProcessOwnerStart' }
        'EDGE-SPLIT-AUTHORITY-STATIC-001 Invoke-DevProcess contains a dynamic or forbidden ProcessStartInfo member access.' { return 'Development.ProcessStartInfoForbiddenMember' }
        'EDGE-SPLIT-AUTHORITY-STATIC-001 Invoke-DevProcess ProcessStartInfo member inventory changed.' { return 'Development.ProcessStartInfoMemberInventory' }
        'EDGE-SPLIT-AUTHORITY-STATIC-001 Invoke-DevProcess TMPDIR overlay rejection changed.' { return 'Development.OverlayTmpdirRejection' }
        'EDGE-SPLIT-AUTHORITY-STATIC-001 Invoke-DevProcess environment overlay assignment changed.' { return 'Development.OverlayAssignment' }
        'EDGE-SPLIT-AUTHORITY-STATIC-001 protected dataflow may not use a scoped variable alias.' { return 'Development.ProtectedScopedAlias' }
        'EDGE-SPLIT-AUTHORITY-STATIC-001 protected dataflow may not escape through a reference.' { return 'Development.ProtectedReference' }
        'EDGE-SPLIT-AUTHORITY-STATIC-001 physical temp helper member invocation allowlist count changed.' { return 'Development.PhysicalTempMemberAllowlist' }
        'EDGE-SPLIT-AUTHORITY-STATIC-001 physical temp helper dataflow variable assignment count changed.' { return 'Development.PhysicalTempDataflow' }
        'EDGE-SPLIT-AUTHORITY-STATIC-001 physical temp helper must return only the canonical physicalRoot.' { return 'Development.PhysicalTempReturn' }
        'EDGE-SPLIT-AUTHORITY-STATIC-001 physical temp helper must set and restore CurrentDirectory exactly once.' { return 'Development.PhysicalTempCurrentDirectory' }
        'EDGE-SPLIT-AUTHORITY-STATIC-001 physical temp helper contains a dynamic/member process or external invocation.' { return 'Development.PhysicalTempDynamicMember' }
        'EDGE-SPLIT-AUTHORITY-STATIC-001 physical temp helper invocation is not the single pre-pin root assignment.' { return 'Development.PhysicalTempInvocation' }
        'EDGE-SPLIT-AUTHORITY-STATIC-001 TMPDIR snapshot dictionary read inventory changed.' { return 'Development.TmpdirSnapshotReadInventory' }
        'EDGE-SPLIT-AUTHORITY-STATIC-001 original TMPDIR presence capture changed.' { return 'Development.TmpdirOriginalPresence' }
        'EDGE-SPLIT-AUTHORITY-STATIC-001 original TMPDIR value capture changed or was overwritten.' { return 'Development.TmpdirOriginalValue' }
        'EDGE-SPLIT-AUTHORITY-STATIC-001 process-scope TMPDIR pin/restore calls are not exact.' { return 'Development.TmpdirProcessCalls' }
        'EDGE-SPLIT-AUTHORITY-STATIC-001 development canonical module imports changed.' { return 'Development.ModuleImport' }
        'EDGE-SPLIT-AUTHORITY-STATIC-001 development validation contains a computed or forbidden Environment invocation.' { return 'Development.EnvironmentInvocation' }
        'EDGE-SPLIT-AUTHORITY-STATIC-001 development command/member owner inventory permits a dynamic, alias, or provider rebinding path.' { return 'Development.CommandOwnerInventory' }
        'EDGE-SPLIT-AUTHORITY-STATIC-001 TMPDIR pin must be the first outer body statement before every executable child launch.' { return 'Development.PinFirst' }
        'EDGE-SPLIT-AUTHORITY-STATIC-001 behavior failure diagnostic flow changed.' { return 'Development.BehaviorDiagnosticFlow' }
        'EDGE-SPLIT-AUTHORITY-STATIC-001 development post-pin main/cleanup flow changed.' { return 'Development.OuterFlowDigest' }
        'EDGE-SPLIT-AUTHORITY-STATIC-001 coordinator/outer cleanup invocations lost their exact pre-restore order.' { return 'Development.CleanupInvocationOrder' }
        'EDGE-SPLIT-AUTHORITY-STATIC-001 cleanup condition/direct call owner changed.' { return 'Development.CleanupDirectOwner' }
        'EDGE-SPLIT-AUTHORITY-STATIC-001 TMPDIR restoration lost its guarded first-failure catch.' { return 'Development.TmpdirFailureCatch' }
        'EDGE-SPLIT-AUTHORITY-STATIC-001 CurrentDirectory restore lost its guarded first-failure catch.' { return 'Development.CurrentDirectoryFailureCatch' }
        'EDGE-SPLIT-AUTHORITY-STATIC-001 protocol module top-level/function inventory count changed.' { return 'Protocol.TopLevelFunctionInventory' }
        'EDGE-SPLIT-AUTHORITY-STATIC-001 protocol module Set-StrictMode statement changed.' { return 'Protocol.SetStrictMode' }
        'EDGE-SPLIT-AUTHORITY-STATIC-001 protocol module ErrorActionPreference statement changed.' { return 'Protocol.ErrorAction' }
        'EDGE-SPLIT-AUTHORITY-STATIC-001 protocol module Add-Type TypeDefinition bytes changed.' { return 'Protocol.AddTypeCanonicalDigest' }
        'EDGE-SPLIT-AUTHORITY-STATIC-001 protocol module export surface shape changed.' { return 'Protocol.ExportSurface' }
        'EDGE-SPLIT-AUTHORITY-STATIC-001 protocol module contains a dynamic command invocation.' { return 'Protocol.DynamicCommand' }
        'EDGE-SPLIT-AUTHORITY-STATIC-001 protocol module contains a dynamic member invocation.' { return 'Protocol.DynamicMember' }
        'EDGE-SPLIT-AUTHORITY-STATIC-001 protocol module pre-pin import roots can reach the cleanup process owner.' { return 'Protocol.PrePinReachability' }
        'EDGE-SPLIT-AUTHORITY-STATIC-001 protocol module Process/ProcessStartInfo construction inventory changed.' { return 'Protocol.ProcessConstruction' }
        default {
            if ($Message.StartsWith(
                    'EDGE-SPLIT-AUTHORITY-STATIC-001 physical temp helper command ',
                    [StringComparison]::Ordinal)) {
                return 'Development.PhysicalTempCommandAllowlist'
            }
            throw "EDGE-SPLIT-AUTHORITY-MUTATION-TARGET-001 unmapped static failure '$Message'."
        }
    }
}

function Assert-EdgePluginContractMutationTargetGuard {
    param(
        [Parameter(Mandatory)][ValidateSet('development', 'protocolModule')]
        [string]$Target,
        [Parameter(Mandatory)][string]$MutationName,
        [Parameter(Mandatory)][string]$TargetOwner,
        [Parameter(Mandatory)][string]$ExpectedShape,
        [Parameter(Mandatory)][string]$MutationSource
    )

    Assert-EdgeStaticMutationShape $Target $MutationName $ExpectedShape $MutationSource
    try {
        if ($Target -ceq 'development') {
            if ($TargetOwner -ceq 'Development.CanonicalSourceDigest') {
                Assert-DevelopmentLifecycleAstGuard -DevelopmentSource $MutationSource
            }
            elseif ($TargetOwner -ceq 'Development.CanonicalFunctionDigest') {
                Assert-DevelopmentLifecycleAstGuard -DevelopmentSource $MutationSource `
                    -SkipSourceDigest
            }
            elseif ($TargetOwner -ceq 'Development.OuterFlowDigest') {
                Assert-DevelopmentLifecycleAstGuard -DevelopmentSource $MutationSource `
                    -SkipSourceDigest -SkipFunctionDigest
            }
            else {
                Assert-DevelopmentLifecycleAstGuard -DevelopmentSource $MutationSource `
                    -SkipSourceDigest -SkipFunctionDigest -SkipOuterFlowDigest
            }
        }
        elseif ($TargetOwner -ceq 'Protocol.AddTypeCanonicalDigest') {
            Assert-ProtocolModuleImportSurfaceAstGuard `
                -ProtocolModuleSource $MutationSource `
                -SkipSourceDigest -SkipFunctionDigest -SkipTopLevelDigest
        }
        else {
            Assert-ProtocolModuleImportSurfaceAstGuard `
                -ProtocolModuleSource $MutationSource `
                -SkipSourceDigest -SkipFunctionDigest -SkipTopLevelDigest `
                -SkipAddTypeDigest -SkipCommandOwnerDigest -SkipMemberOwnerDigest
        }
        throw "EDGE-SPLIT-AUTHORITY-MUTATION-TARGET-001 '$MutationName' was accepted by its dedicated target owner."
    }
    catch {
        if (-not $_.Exception.Message.StartsWith(
                'EDGE-SPLIT-AUTHORITY-STATIC-001', [StringComparison]::Ordinal)) {
            throw
        }
        $actualOwner = Get-EdgeStaticMutationFailureOwner $_.Exception.Message
        if ($actualOwner -cne $TargetOwner) {
            throw "EDGE-SPLIT-AUTHORITY-MUTATION-TARGET-001 '$MutationName' expected '$TargetOwner' but reached '$actualOwner': $($_.Exception.Message)"
        }
        return [pscustomobject][ordered]@{
            mutationName = $MutationName
            target = $Target
            targetOwner = $actualOwner
            shapePredicate = $ExpectedShape
            passed = $true
        }
    }
}

function Assert-DeterministicBuildClosureStaticGuard {
    param(
        [Parameter(Mandatory)][string]$ProtocolModuleSource,
        [Parameter(Mandatory)][string]$GeneratorSource,
        [Parameter(Mandatory)][string]$ValidatorSource,
        [Parameter(Mandatory)][string]$DeterministicTargetsSource
    )

    $sourceContracts = @(
        [pscustomobject][ordered]@{
            name = 'generator'
            source = $GeneratorSource
            size = 206672
            sha256 = 'af58f86fb4e4fa0de343d16bcb58e4365f666bf0668fbb3dfc5068f7e7841e5d'
        },
        [pscustomobject][ordered]@{
            name = 'validator'
            source = $ValidatorSource
            size = 245970
            sha256 = '49881fe6e6fbb2e2e1d92415621c6908681a24a53625b0b59c45a39cbafd2a15'
        },
        [pscustomobject][ordered]@{
            name = 'deterministic targets'
            source = $DeterministicTargetsSource
            size = 4268
            sha256 = '24aeb37fc1ff9d82f4290e395d283cb983a9d3e6ccc9a265ed350935e73d8ba4'
        }
    )
    foreach ($sourceContract in $sourceContracts) {
        $bytes = [Text.UTF8Encoding]::new($false).GetBytes([string]$sourceContract.source)
        $digest = [Convert]::ToHexString(
            [Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
        if ($bytes.Length -ne [int]$sourceContract.size -or
            $digest -cne [string]$sourceContract.sha256) {
            throw "EDGE-SPLIT-AUTHORITY-STATIC-001 $($sourceContract.name) deterministic-build closure bytes changed."
        }
    }

    try { [xml]$targetsDocument = $DeterministicTargetsSource }
    catch {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 deterministic build targets are not well-formed XML.'
    }
    $namespaceManager = [Xml.XmlNamespaceManager]::new($targetsDocument.NameTable)
    $namespaceManager.AddNamespace('msb', 'http://schemas.microsoft.com/developer/msbuild/2003')
    $importTargets = @($targetsDocument.SelectNodes(
            '/msb:Project/msb:Target[@Name="EdgePluginContractDeterministicBuildImported"]',
            $namespaceManager))
    $avaloniaTargets = @($targetsDocument.SelectNodes(
            '/msb:Project/msb:Target[@Name="CompileAvaloniaXaml"]',
            $namespaceManager))
    if ($importTargets.Count -ne 1 -or $avaloniaTargets.Count -ne 1 -or
        [string]$importTargets[0].BeforeTargets -cne 'CoreCompile' -or
        [string]$avaloniaTargets[0].DependsOnTargets -cne
            'EdgePluginContractDeterministicBuildImported;$(CompileAvaloniaXamlDependsOn)') {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 deterministic targets lost their authority/Avalonia target ownership.'
    }
    $projectDirectoryBindings = @($avaloniaTargets[0].SelectNodes(
            'msb:CompileAvaloniaXamlTask[@ProjectDirectory="$(_EdgeContractVirtualProjectDirectory)"]',
            $namespaceManager))
    $intermediateRefBindings = @($avaloniaTargets[0].SelectNodes(
            'msb:ItemGroup/msb:IntermediateRefAssembly[@AvaloniaCompileOutput="%(FullPath)"]',
            $namespaceManager))
    $authorityErrors = @($importTargets[0].SelectNodes(
            'msb:Error[contains(@Condition, "_EdgeContractAuthorityBuild") or contains(@Condition, "_EdgeContractRepositoryRoot") or contains(@Condition, "_EdgeContractProjectRelativeDirectoryNormalized")]',
            $namespaceManager))
    if ($projectDirectoryBindings.Count -ne 1 -or
        [string]$projectDirectoryBindings[0].RefAssemblyFile -cne '@(IntermediateRefAssembly)' -or
        $intermediateRefBindings.Count -ne 1 -or $authorityErrors.Count -ne 3 -or
        -not $DeterministicTargetsSource.Contains(
            "`$([MSBuild]::MakeRelative('`$(_EdgeContractRepositoryRoot)', '`$(MSBuildProjectDirectory)'))",
            [StringComparison]::Ordinal) -or
        -not $DeterministicTargetsSource.Contains(
            "[System.IO.Path]::IsPathRooted('`$(_EdgeContractProjectRelativeDirectoryNormalized)')",
            [StringComparison]::Ordinal)) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 deterministic targets lost virtual-root/ref-assembly escape guards.'
    }

    foreach ($sourceRow in @(
            [pscustomobject]@{
                name = 'generator'; source = $GeneratorSource
                vector = '$canonicalDeterministicBuildArguments'
                embedded = 'Assert-CanonicalEmbeddedDebugIdentity'
                collector = 'Get-MsBuildAuthorityInputs'
            },
            [pscustomobject]@{
                name = 'validator'; source = $ValidatorSource
                vector = '$validatorDeterministicBuildArguments'
                embedded = 'Assert-IndependentEmbeddedDebugIdentity'
                collector = 'Get-IndependentMsBuildAuthorityInventory'
            })) {
        $source = [string]$sourceRow.source
        $buildVectorUseCount = [Text.RegularExpressions.Regex]::Matches(
                $source,
                [Text.RegularExpressions.Regex]::Escape(") + $([string]$sourceRow.vector) + @("),
                [Text.RegularExpressions.RegexOptions]::CultureInvariant).Count
        if ($buildVectorUseCount -ne 3) {
            throw "EDGE-SPLIT-AUTHORITY-STATIC-001 $($sourceRow.name) lost one of three deterministic build-vector bindings."
        }
        foreach ($needle in @(
                "[Parameter(Mandatory = `$true)][string[]]`$DeterministicBuildArguments",
                "'-p:TargetsTriggeredByCompilation='",
                "'-getProperty:TargetPath,TargetRefPath,IntermediateAssembly,IntermediateOutputPath,TargetFileName'",
                "'-getItem:IntermediateRefAssembly'",
                'mutated compiled output bytes',
                ') + $DeterministicBuildArguments + @("-preprocess:',
                "'-p:DebugType=embedded'",
                ".Replace('%', '%25').Replace(';', '%3B').Replace(',', '%2C')",
                ".Replace('=', '==').Replace(',', ',,')",
                'eng/EdgePluginContractDeterministicBuild.targets',
                'deterministic-build-targets',
                [string]$sourceRow.embedded,
                [string]$sourceRow.collector)) {
            if (-not $source.Contains($needle, [StringComparison]::Ordinal)) {
                throw "EDGE-SPLIT-AUTHORITY-STATIC-001 $($sourceRow.name) deterministic-build semantic binding is missing: $needle."
            }
        }
    }
    if (-not $GeneratorSource.Contains(
            '$canonicalRawPathMap = "$(ConvertTo-CanonicalPathMapSourceToken $repositoryRoot)=/_,$(ConvertTo-CanonicalPathMapSourceToken $generatedRoot)=/__edge_contract_generated__"',
            [StringComparison]::Ordinal) -or
        -not $ValidatorSource.Contains(
            '$validatorRawPathMap = "$(ConvertTo-IndependentPathMapSourceToken $RepositoryRoot)=/_,$(ConvertTo-IndependentPathMapSourceToken $validatorGeneratedRoot)=/__edge_contract_generated__"',
            [StringComparison]::Ordinal) -or
        -not $ValidatorSource.Contains(
            'Assert-JsonEqual $inputFact $actualFact "PE input identity/size/SHA/MVID differs from raw bytes:',
            [StringComparison]::Ordinal) -or
        -not $ProtocolModuleSource.Contains(
            "'eng/EdgePluginContractDeterministicBuild.targets',",
            [StringComparison]::Ordinal) -or
        -not $ProtocolModuleSource.Contains(
            "'scripts/tests/EdgePluginContractStaticGuard.psm1',",
            [StringComparison]::Ordinal) -or
        -not $ProtocolModuleSource.Contains(
            "'scripts/tests/Test-EdgePluginContractStaticGuard.ps1',",
            [StringComparison]::Ordinal)) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 deterministic PathMap/raw-PE/authority-code binding changed.'
    }
}

function Assert-FormalValidationStaticGuard {
    param(
        [Parameter(Mandatory)][string]$FormalSource,
        [Parameter(Mandatory)][string]$ProtocolModuleSource,
        [switch]$SkipSourceDigest
    )

    $tokens = $null
    $parseErrors = $null
    $formalAst = [System.Management.Automation.Language.Parser]::ParseInput(
        $FormalSource, [ref]$tokens, [ref]$parseErrors)
    if (@($parseErrors).Count -ne 0) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 formal validation source has PowerShell parse errors.'
    }
    if ($null -eq $formalAst.ParamBlock -or
        @($formalAst.ParamBlock.Parameters).Count -ne 0 -or
        @($formalAst.ParamBlock.Attributes).Count -ne 1 -or
        [string]$formalAst.ParamBlock.Attributes[0].TypeName.FullName -cne 'CmdletBinding') {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 formal validation entry must expose no override parameters.'
    }
    foreach ($forbidden in @(
            'Generate-EdgePluginContractLedger.ps1',
            'CommandOverride', 'ChildExecutableOverride', 'ChildScriptOverride',
            'FixtureMode', 'SkipAuthority', 'SkipReplay', 'SkipValidation')) {
        if ($FormalSource.Contains($forbidden, [StringComparison]::Ordinal)) {
            throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 formal validation exposes a generator/fixture/skip/command override.'
        }
    }
    $requiredExactSources = [string[]]@(
        "`$canonicalLedgerRelativePath = 'eng/baselines/edge-plugin-contract-ledger.json'",
        "`$staticGuardResult.sourceCount -ne 11",
        "`$staticGuardResult.sourceDigests.formal -cne `$formalSourceSha256",
        "`$request = [pscustomobject][ordered]@{`n    schemaVersion = 1`n    mode = 'formal-clean'`n    runId = `$runId",
        "'ls-files', '--stage', '-z'",
        "'diff', '--cached', '--quiet', '--no-ext-diff', 'HEAD', '--'",
        "'rev-list', '--count'",
        '$implementationHead..$($state.head)',
        "'rev-list', '--parents', '-n', '1'",
        "'diff-tree', '--no-commit-id', '--name-only', '-r', '-z'",
        '100644 blob [0-9a-f]{40}\teng/baselines/edge-plugin-contract-ledger\.json',
        "`$committedLedgerBytes, [byte[]]`$state.ledgerBytes",
        "`$confirmedPreconditions = Assert-FormalValidationPreconditions",
        "Move-Item -LiteralPath `$partialOuterMarkerPath -Destination `$outerMarkerPath",
        "function Remove-FormalUnmarkedOuterRunRoot {",
        "Remove-EdgeFormalAuthorityRunState",
        "function Assert-FormalReceiptIdentity {",
        "-RequireAuthorityReceipt', '-RequireFormalAuthorityReceipt'",
        "`$postValidationState = Get-FormalRepositoryState",
        "`$finalState = Get-FormalRepositoryState",
        "`$finalReceiptSha256 = Assert-FormalReceiptIdentity",
        "`$formalResult = [pscustomobject][ordered]@{",
        "`$formalResultBytes = ConvertTo-EdgeCanonicalBytes `$formalResult",
        "Write-Output (ConvertTo-EdgeCanonicalJson `$formalResult)")
    foreach ($needle in $requiredExactSources) {
        if ([Text.RegularExpressions.Regex]::Matches(
                $FormalSource, [Text.RegularExpressions.Regex]::Escape($needle),
                [Text.RegularExpressions.RegexOptions]::CultureInvariant).Count -ne 1) {
            throw "EDGE-SPLIT-AUTHORITY-STATIC-001 formal validation fixed contract changed: $needle"
        }
    }

    $getOwningFunction = {
        param([object]$Node)
        $current = $Node.Parent
        while ($null -ne $current) {
            if ($current -is [System.Management.Automation.Language.FunctionDefinitionAst]) {
                return $current
            }
            $current = $current.Parent
        }
        return $null
    }
    $allFormalCommands = @($formalAst.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.CommandAst]
            }, $true))
    $runtimeCommands = @($allFormalCommands | Where-Object {
            $null -eq (& $getOwningFunction $_)
        })
    $newItemCommands = @($runtimeCommands | Where-Object {
            [string]::Equals(
                [string]$_.GetCommandName(), 'New-Item',
                [StringComparison]::OrdinalIgnoreCase)
        })
    if ($newItemCommands.Count -ne 1 -or
        [string]$newItemCommands[0].Extent.Text -cne
            'New-Item -ItemType Directory -Path $outerRunRoot') {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 formal validation directory creation owner changed.'
    }
    $confirmedOffset = $FormalSource.IndexOf(
        '$confirmedPreconditions = Assert-FormalValidationPreconditions',
        [StringComparison]::Ordinal)
    $confirmedStateOffset = $FormalSource.IndexOf(
        "-ErrorCode 'EDGE-SPLIT-AUTHORITY-FORMAL-PRECONDITION'",
        $confirmedOffset, [StringComparison]::Ordinal)
    if ($confirmedOffset -lt 0 -or $confirmedStateOffset -lt $confirmedOffset -or
        $confirmedStateOffset -ge $newItemCommands[0].Extent.StartOffset) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 formal validation side effects precede the second precondition/state proof.'
    }

    $formalProcessCalls = @($runtimeCommands | Where-Object {
            [string]::Equals(
                [string]$_.GetCommandName(), 'Invoke-FormalProcess',
                [StringComparison]::OrdinalIgnoreCase)
        })
    $coordinatorCalls = @($formalProcessCalls | Where-Object {
            [string]::Equals(
                [string]$_.GetCommandName(), 'Invoke-FormalProcess',
                [StringComparison]::OrdinalIgnoreCase) -and
            $_.Extent.Text.Contains(
                'Invoke-EdgePluginContractAuthorityCoordinator.ps1',
                [StringComparison]::Ordinal)
        })
    $fastConsumerCalls = @($formalProcessCalls | Where-Object {
            [string]::Equals(
                [string]$_.GetCommandName(), 'Invoke-FormalProcess',
                [StringComparison]::OrdinalIgnoreCase) -and
            $_.Extent.Text.Contains(
                'Test-EdgePluginContractLedger.ps1',
                [StringComparison]::Ordinal)
        })
    if ($formalProcessCalls.Count -ne 2 -or
        $coordinatorCalls.Count -ne 1 -or $fastConsumerCalls.Count -ne 1 -or
        -not [object]::ReferenceEquals($formalProcessCalls[0], $coordinatorCalls[0]) -or
        -not [object]::ReferenceEquals($formalProcessCalls[1], $fastConsumerCalls[0]) -or
        $coordinatorCalls[0].Extent.StartOffset -le $newItemCommands[0].Extent.StartOffset -or
        $fastConsumerCalls[0].Extent.StartOffset -le $coordinatorCalls[0].Extent.StartOffset -or
        -not $fastConsumerCalls[0].Extent.Text.Contains(
            "'-RequireAuthorityReceipt', '-RequireFormalAuthorityReceipt'",
            [StringComparison]::Ordinal)) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 formal validation coordinator/fast-consumer launch contract changed.'
    }
    $receiptCalls = @($runtimeCommands | Where-Object {
            [string]::Equals(
                [string]$_.GetCommandName(), 'Assert-EdgeAuthorityReceipt',
                [StringComparison]::OrdinalIgnoreCase)
        })
    $receiptIdentityCalls = @($runtimeCommands | Where-Object {
            [string]::Equals(
                [string]$_.GetCommandName(), 'Assert-FormalReceiptIdentity',
                [StringComparison]::OrdinalIgnoreCase)
        })
    $invalidFormalReceiptSwitches = @($receiptCalls | Where-Object {
            $formalSwitches = @($_.CommandElements | Where-Object {
                    $_ -is [System.Management.Automation.Language.CommandParameterAst] -and
                    [string]::Equals(
                        [string]$_.ParameterName, 'RequireFormal',
                        [StringComparison]::OrdinalIgnoreCase)
                })
            $formalSwitches.Count -ne 1 -or
            $null -ne $formalSwitches[0].Argument
        })
    if ($receiptCalls.Count -ne 2 -or
        $receiptIdentityCalls.Count -ne 2 -or
        $invalidFormalReceiptSwitches.Count -ne 0 -or
        @($receiptCalls | Where-Object {
                -not $_.Extent.Text.Contains('-RequireFormal', [StringComparison]::Ordinal)
            }).Count -ne 0 -or
        @($receiptIdentityCalls | Where-Object {
                -not $_.Extent.Text.Contains(
                    '-ReceiptPath $receiptPath', [StringComparison]::Ordinal) -or
                -not $_.Extent.Text.Contains(
                    '-ExpectedSha256 ([string]$descriptor.receiptSha256)',
                    [StringComparison]::Ordinal)
            }).Count -ne 0 -or
        $receiptCalls[0].Extent.StartOffset -ge $fastConsumerCalls[0].Extent.StartOffset) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 formal validation signed formal receipt contract changed.'
    }
    $cleanupCalls = @($runtimeCommands | Where-Object {
            [string]::Equals(
                [string]$_.GetCommandName(), 'Remove-EdgeFormalAuthorityRunState',
                [StringComparison]::OrdinalIgnoreCase)
        })
    $unmarkedCleanupCalls = @($runtimeCommands | Where-Object {
            [string]::Equals(
                [string]$_.GetCommandName(), 'Remove-FormalUnmarkedOuterRunRoot',
                [StringComparison]::OrdinalIgnoreCase)
        })
    $formalRemoveCommands = @($allFormalCommands | Where-Object {
            [string]::Equals(
                [string]$_.GetCommandName(), 'Remove-Item',
                [StringComparison]::OrdinalIgnoreCase)
        })
    if ($cleanupCalls.Count -ne 1 -or $unmarkedCleanupCalls.Count -ne 1 -or
        -not $unmarkedCleanupCalls[0].Extent.Text.Contains(
            '-PartialMarkerPath $partialOuterMarkerPath',
            [StringComparison]::Ordinal) -or
        -not $unmarkedCleanupCalls[0].Extent.Text.Contains(
            '-RunId $runId', [StringComparison]::Ordinal) -or
        @($formalRemoveCommands | Where-Object {
                @($_.CommandElements | Where-Object {
                        $_ -is [System.Management.Automation.Language.CommandParameterAst] -and
                        [string]::Equals(
                            [string]$_.ParameterName, 'Recurse',
                            [StringComparison]::OrdinalIgnoreCase)
                    }).Count -ne 0
            }).Count -ne 0) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 formal validation cleanup must remain exact and non-recursive.'
    }

    $protocolTokens = $null
    $protocolErrors = $null
    $protocolAst = [System.Management.Automation.Language.Parser]::ParseInput(
        $ProtocolModuleSource, [ref]$protocolTokens, [ref]$protocolErrors)
    $formalCleanupFunctions = @($protocolAst.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
                [string]::Equals(
                    [string]$node.Name, 'Remove-EdgeFormalAuthorityRunState',
                    [StringComparison]::OrdinalIgnoreCase)
            }, $true))
    if (@($protocolErrors).Count -ne 0 -or $formalCleanupFunctions.Count -ne 1) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 protocol formal cleanup owner is missing or invalid.'
    }
    $formalCleanupSource = [string]$formalCleanupFunctions[0].Extent.Text
    if ($formalCleanupSource.Contains('-Recurse', [StringComparison]::OrdinalIgnoreCase) -or
        -not $formalCleanupSource.Contains(
            'real authority repository must remain outside the deletable formal run root',
            [StringComparison]::Ordinal) -or
        -not $formalCleanupSource.Contains(
            'an unregistered formal worktree path survived',
            [StringComparison]::Ordinal) -or
        -not $formalCleanupSource.Contains(
            "Join-Path `$run 'authority.json'", [StringComparison]::Ordinal) -or
        -not $formalCleanupSource.Contains(
            "Join-Path `$run 'replay.json'", [StringComparison]::Ordinal)) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 protocol formal cleanup fail-closed allowlist changed.'
    }
    $formalCleanupBytes = [Text.UTF8Encoding]::new($false).GetBytes($formalCleanupSource)
    $formalCleanupDigest = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData($formalCleanupBytes)).ToLowerInvariant()
    if ($formalCleanupBytes.Length -ne 9611 -or
        $formalCleanupDigest -cne 'd013b7a344799ed2f976d750a0fc16ada9dd36da4ef40f5f38f3496fc7cb8edd') {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 protocol formal cleanup canonical extent bytes changed.'
    }

    $postValidationOffset = $FormalSource.IndexOf(
        '$postValidationState = Get-FormalRepositoryState', [StringComparison]::Ordinal)
    $finallyOffset = $FormalSource.IndexOf('finally {',
        $postValidationOffset, [StringComparison]::Ordinal)
    $finalStateOffset = $FormalSource.IndexOf(
        '$finalState = Get-FormalRepositoryState', $finallyOffset,
        [StringComparison]::Ordinal)
    $lastReceiptOffset = $receiptCalls[1].Extent.StartOffset
    $lastReceiptDigestOffset = $FormalSource.IndexOf(
        '$finalReceiptSha256 = Assert-FormalReceiptIdentity',
        $lastReceiptOffset, [StringComparison]::Ordinal)
    $resultOffset = $FormalSource.IndexOf(
        '$formalResult = [pscustomobject][ordered]@{', [StringComparison]::Ordinal)
    $resultBytesOffset = $FormalSource.IndexOf(
        '$formalResultBytes = ConvertTo-EdgeCanonicalBytes $formalResult',
        [StringComparison]::Ordinal)
    $outputOffset = $FormalSource.IndexOf(
        'Write-Output (ConvertTo-EdgeCanonicalJson $formalResult)',
        [StringComparison]::Ordinal)
    if ($postValidationOffset -lt 0 -or $finallyOffset -le $postValidationOffset -or
        $finalStateOffset -le $finallyOffset -or
        $receiptIdentityCalls[0].Extent.StartOffset -le $finalStateOffset -or
        $receiptIdentityCalls[0].Extent.EndOffset -ge $lastReceiptOffset -or
        $lastReceiptOffset -le $finalStateOffset -or
        $lastReceiptDigestOffset -le $lastReceiptOffset -or
        $receiptIdentityCalls[1].Extent.StartOffset -le $lastReceiptDigestOffset -or
        $receiptIdentityCalls[1].Extent.EndOffset -ge $resultOffset -or
        $resultOffset -le $lastReceiptDigestOffset -or
        $resultBytesOffset -le $resultOffset -or $outputOffset -le $resultBytesOffset -or
        $receiptCalls[1].Extent.EndOffset -ge $lastReceiptDigestOffset) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 formal validation post-state/receipt/result order changed.'
    }
    $topLevelStatements = @($formalAst.EndBlock.Statements)
    if ($topLevelStatements.Count -eq 0 -or
        -not [object]::ReferenceEquals(
            $topLevelStatements[-1],
            @($runtimeCommands | Where-Object {
                    [string]::Equals(
                        [string]$_.GetCommandName(), 'Write-Output',
                        [StringComparison]::OrdinalIgnoreCase)
                })[-1].Parent)) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 formal validation result output is not the final statement.'
    }
    if (-not $SkipSourceDigest) {
        $formalSourceBytes = [Text.UTF8Encoding]::new($false).GetBytes($FormalSource)
        $formalSourceDigest = [Convert]::ToHexString(
            [Security.Cryptography.SHA256]::HashData($formalSourceBytes)).ToLowerInvariant()
        if ($formalSourceBytes.Length -ne 31929 -or
            $formalSourceDigest -cne 'feee22e5896219a9e1d318683fc45d213ef5302761819cdbe28c2d3a4688100d') {
            throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 formal validation canonical source bytes changed.'
        }
    }
}

function Assert-ProductionCoordinatorStaticGuard {
    param(
        [Parameter(Mandatory)][string]$CoordinatorSource,
        [Parameter(Mandatory)][string]$DevelopmentSource,
        [Parameter(Mandatory)][string]$FormalSource,
        [Parameter(Mandatory)][string]$ProtocolModuleSource,
        [Parameter(Mandatory)][string]$GeneratorSource,
        [Parameter(Mandatory)][string]$RequiredWrapperSource,
        [Parameter(Mandatory)][string]$ValidatorSource,
        [Parameter(Mandatory)][string]$BehaviorSource,
        [Parameter(Mandatory)][string]$RequiredXunitSource,
        [Parameter(Mandatory)][string]$DeterministicTargetsSource,
        [Parameter(Mandatory)][string]$MutationRunnerSource,
        [switch]$SkipFormalSourceDigest
    )
    $mutationRunnerBytes = [Text.UTF8Encoding]::new($false).GetBytes($MutationRunnerSource)
    $mutationRunnerDigest = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData($mutationRunnerBytes)).ToLowerInvariant()
    if ($mutationRunnerBytes.Length -ne 105752 -or
        $mutationRunnerDigest -cne '1d16e4ba85176a7cb2f3374d38ee5db7931e28221443f756f33361f4f17f43d7') {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 persisted static mutation owner bytes changed.'
    }
    $behaviorCanonicalBindingNeedles = [string[]]@(
        "[byte[]]`$canonicalBindingBytes = @(`n    ConvertTo-EdgeCanonicalBytes `$authorityBindings)",
        "[Security.Cryptography.CryptographicOperations]::FixedTimeEquals(`n        `$bindingBytes, `$canonicalBindingBytes)")
    foreach ($needle in $behaviorCanonicalBindingNeedles) {
        if ([Text.RegularExpressions.Regex]::Matches(
                $BehaviorSource, [Text.RegularExpressions.Regex]::Escape($needle),
                [Text.RegularExpressions.RegexOptions]::CultureInvariant).Count -ne 1) {
            throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 behavior canonical binding byte-array shape changed.'
        }
    }
    $behaviorReplayFixtureProtocolNeedles = [string[]]@(
        "`$fixtureProtocolModulePath = Join-Path `$gitRoot 'scripts/tests/EdgePluginContractLedger.Protocol.psm1'",
        '[IO.File]::WriteAllBytes($fixtureProtocolModulePath, [IO.File]::ReadAllBytes($protocolModulePath))',
        "            'scripts/tests/EdgePluginContractLedger.Protocol.psm1',")
    foreach ($needle in $behaviorReplayFixtureProtocolNeedles) {
        if ([Text.RegularExpressions.Regex]::Matches(
                $BehaviorSource, [Text.RegularExpressions.Regex]::Escape($needle),
                [Text.RegularExpressions.RegexOptions]::CultureInvariant).Count -ne 1) {
            throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 behavior replay fixture protocol dependency changed.'
        }
    }
    $behaviorPackageCountStart = $BehaviorSource.IndexOf(
        "        'package-entry-count-forged' = {", [StringComparison]::Ordinal)
    $behaviorPackageCountEnd = $BehaviorSource.IndexOf(
        "        'summary-four-layer-count-forged' = {", [StringComparison]::Ordinal)
    if ($behaviorPackageCountStart -lt 0 -or
        $behaviorPackageCountEnd -le $behaviorPackageCountStart -or
        [Text.RegularExpressions.Regex]::Matches(
            $BehaviorSource,
            [Text.RegularExpressions.Regex]::Escape("        'package-entry-count-forged' = {"),
            [Text.RegularExpressions.RegexOptions]::CultureInvariant).Count -ne 1 -or
        [Text.RegularExpressions.Regex]::Matches(
            $BehaviorSource,
            [Text.RegularExpressions.Regex]::Escape("        'summary-four-layer-count-forged' = {"),
            [Text.RegularExpressions.RegexOptions]::CultureInvariant).Count -ne 1) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 behavior package count fixture body changed.'
    }
    $behaviorPackageCountSource = $BehaviorSource.Substring(
        $behaviorPackageCountStart,
        $behaviorPackageCountEnd - $behaviorPackageCountStart)
    $behaviorPackageCountBytes = [Text.UTF8Encoding]::new($false).GetBytes(
        $behaviorPackageCountSource)
    $behaviorPackageCountDigest = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData(
            $behaviorPackageCountBytes)).ToLowerInvariant()
    if ($behaviorPackageCountBytes.Length -ne 1663 -or
        $behaviorPackageCountDigest -cne
            '44c7f7f8a5a3df28fb2587ef99bb14f73d7e26cbee8980bd8b7f8ea1231fcba1') {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 behavior package count fixture body changed.'
    }
    $behaviorTailMutationStartAnchor = "        'credentialed-historical-url' = {"
    $behaviorTailMutationEndAnchor = "    `$expectedCodes = [ordered]@{"
    $behaviorTailExpectedStartAnchor =
        "        'credentialed-historical-url' = 'EDGE-SPLIT-LEDGER-FAST-PUBLISHED-URI'"
    $behaviorTailExpectedEndAnchor = "`n    }`n    if (`$mutants.Count"
    foreach ($anchor in [string[]]@(
            $behaviorTailMutationStartAnchor,
            $behaviorTailMutationEndAnchor,
            $behaviorTailExpectedStartAnchor,
            $behaviorTailExpectedEndAnchor)) {
        if ([Text.RegularExpressions.Regex]::Matches(
                $BehaviorSource, [Text.RegularExpressions.Regex]::Escape($anchor),
                [Text.RegularExpressions.RegexOptions]::CultureInvariant).Count -ne 1) {
            throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 behavior tail semantic fixture contract changed.'
        }
    }
    $behaviorTailMutationStart = $BehaviorSource.IndexOf(
        $behaviorTailMutationStartAnchor, [StringComparison]::Ordinal)
    $behaviorTailMutationEnd = $BehaviorSource.IndexOf(
        $behaviorTailMutationEndAnchor, [StringComparison]::Ordinal)
    $behaviorTailExpectedStart = $BehaviorSource.IndexOf(
        $behaviorTailExpectedStartAnchor, [StringComparison]::Ordinal)
    $behaviorTailExpectedEndStart = $BehaviorSource.IndexOf(
        $behaviorTailExpectedEndAnchor, $behaviorTailExpectedStart,
        [StringComparison]::Ordinal)
    $behaviorTailExpectedClosure = "`n    }"
    $behaviorTailExpectedEnd =
        $behaviorTailExpectedEndStart + $behaviorTailExpectedClosure.Length
    if ($behaviorTailMutationStart -lt 0 -or
        $behaviorTailMutationEnd -le $behaviorTailMutationStart -or
        $behaviorTailExpectedStart -lt 0 -or
        $behaviorTailExpectedEndStart -le $behaviorTailExpectedStart) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 behavior tail semantic fixture contract changed.'
    }
    $behaviorTailMutationSource = $BehaviorSource.Substring(
        $behaviorTailMutationStart,
        $behaviorTailMutationEnd - $behaviorTailMutationStart)
    $behaviorTailExpectedSource = $BehaviorSource.Substring(
        $behaviorTailExpectedStart,
        $behaviorTailExpectedEnd - $behaviorTailExpectedStart)
    $behaviorTailContractSource =
        $behaviorTailMutationSource + "--expected--`n" + $behaviorTailExpectedSource
    $behaviorTailContractBytes = [Text.UTF8Encoding]::new($false).GetBytes(
        $behaviorTailContractSource)
    $behaviorTailContractDigest = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData(
            $behaviorTailContractBytes)).ToLowerInvariant()
    if ($behaviorTailContractBytes.Length -ne 718 -or
        $behaviorTailContractDigest -cne
            '7656c8f49428daab8f83281ff902e7cd4df4358186d9725e3233ebc4c39b7c30') {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 behavior tail semantic fixture contract changed.'
    }
    $validatorCommitPairOrderingNeedles = [string[]]@(
        "if (-not `$CommitPairGateOnly) {`n    `$deterministicBuildTargetsPath = Resolve-RepositoryPath 'eng/EdgePluginContractDeterministicBuild.targets'",
        "if ((Get-Sha256 `$deterministicBuildTargetsPath) -cne `$deterministicBuildTargetsSha256) {`n        throw 'EDGE-SPLIT-LEDGER-001 deterministic authority build targets digest differs from the independent pinned contract.'")
    foreach ($needle in $validatorCommitPairOrderingNeedles) {
        if ([Text.RegularExpressions.Regex]::Matches(
                $ValidatorSource, [Text.RegularExpressions.Regex]::Escape($needle),
                [Text.RegularExpressions.RegexOptions]::CultureInvariant).Count -ne 1) {
            throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 validator CommitPairGateOnly deterministic-target ordering changed.'
        }
    }
    Assert-DevelopmentLifecycleAstGuard -DevelopmentSource $DevelopmentSource
    Assert-FormalValidationStaticGuard `
        -FormalSource $FormalSource -ProtocolModuleSource $ProtocolModuleSource `
        -SkipSourceDigest:$SkipFormalSourceDigest
    Assert-ProtocolModuleImportSurfaceAstGuard -ProtocolModuleSource $ProtocolModuleSource
    Assert-DeterministicBuildClosureStaticGuard `
        -ProtocolModuleSource $ProtocolModuleSource `
        -GeneratorSource $GeneratorSource `
        -ValidatorSource $ValidatorSource `
        -DeterministicTargetsSource $DeterministicTargetsSource
    if (-not [Text.RegularExpressions.Regex]::IsMatch(
            $CoordinatorSource, '(?s)^\[CmdletBinding\(\)\]\s*param\(\)',
            [Text.RegularExpressions.RegexOptions]::CultureInvariant) -or
        -not $CoordinatorSource.Contains('$startInfo.FileName = $script:coordinatorPowerShellPath', [StringComparison]::Ordinal) -or
        -not $CoordinatorSource.Contains('$coordinatorPowerShellPath = Resolve-EdgeFixedExecutable ([Environment]::ProcessPath)', [StringComparison]::Ordinal) -or
        -not $CoordinatorSource.Contains('$coordinatorGitPath = Assert-EdgeAuthorityFinalGitExecutablePath', [StringComparison]::Ordinal) -or
        -not $CoordinatorSource.Contains('$coordinatorParentBinding = Initialize-EdgeAuthorityCoordinatorParentEnvironment', [StringComparison]::Ordinal) -or
        -not $CoordinatorSource.Contains('Assert-EdgeAuthorityCoordinatorParentRequest -Binding $coordinatorParentBinding -Request $request', [StringComparison]::Ordinal) -or
        -not $CoordinatorSource.Contains('$startInfo.FileName = $script:coordinatorGitPath', [StringComparison]::Ordinal) -or
        -not $CoordinatorSource.Contains('New-EdgeAuthorityGitChildEnvironment', [StringComparison]::Ordinal) -or
        -not $CoordinatorSource.Contains('EdgeAuthorityBoundedStreamCapture', [StringComparison]::Ordinal) -or
        -not $CoordinatorSource.Contains('$capture = Wait-EdgeBoundedCaptureTasks $stdoutTask $stderrTask $deadline', [StringComparison]::Ordinal) -or
        -not $CoordinatorSource.Contains('$capture = Wait-EdgeBoundedCaptureTasks $Child.stdoutTask $Child.stderrTask $CaptureDeadlineUtc', [StringComparison]::Ordinal) -or
        -not $CoordinatorSource.Contains("@('rev-parse', '--show-toplevel')", [StringComparison]::Ordinal) -or
        -not $CoordinatorSource.Contains('Get-CoordinatorLocalGitConfigDigest', [StringComparison]::Ordinal) -or
        -not $CoordinatorSource.Contains("'scripts/tests/Test-EdgePluginContractLedger.ps1'", [StringComparison]::Ordinal) -or
        -not $CoordinatorSource.Contains("'eng/Generate-EdgePluginContractLedger.ps1'", [StringComparison]::Ordinal) -or
        -not $CoordinatorSource.Contains("'-AuthorityRebuildOnly'", [StringComparison]::Ordinal) -or
        -not $DevelopmentSource.Contains('$coordinatorRunRoot = Join-Path $outerRunRoot', [StringComparison]::Ordinal) -or
        -not $DevelopmentSource.Contains('reserved child path was not absent', [StringComparison]::Ordinal) -or
        -not $DevelopmentSource.Contains("@('rev-parse', '--show-toplevel')", [StringComparison]::Ordinal) -or
        -not $DevelopmentSource.Contains('EdgeAuthorityBoundedStreamCapture', [StringComparison]::Ordinal) -or
        -not $DevelopmentSource.Contains('$capture = Wait-EdgeBoundedCaptureTasks $stdoutTask $stderrTask $deadline', [StringComparison]::Ordinal) -or
        -not $DevelopmentSource.Contains('Get-DevLocalGitConfigDigest', [StringComparison]::Ordinal) -or
        -not $DevelopmentSource.Contains('schemaVersion = 2', [StringComparison]::Ordinal) -or
        -not $DevelopmentSource.Contains('fixedGitExecutablePath = $gitPath', [StringComparison]::Ordinal) -or
        -not $DevelopmentSource.Contains('pinnedPathSha256 = Get-EdgeSha256Text $devPinnedPath', [StringComparison]::Ordinal) -or
        -not $DevelopmentSource.Contains('$coordinatorEnvironment = New-EdgeAuthorityCoordinatorParentEnvironment', [StringComparison]::Ordinal) -or
        -not $DevelopmentSource.Contains('-Environment $coordinatorEnvironment', [StringComparison]::Ordinal) -or
        -not $ProtocolModuleSource.Contains('[StringComparer]::OrdinalIgnoreCase', [StringComparison]::Ordinal) -or
        -not $ProtocolModuleSource.Contains("'GIT_NAMESPACE'", [StringComparison]::Ordinal) -or
        -not $ProtocolModuleSource.Contains("'GIT_LITERAL_PATHSPECS'", [StringComparison]::Ordinal) -or
        -not $ProtocolModuleSource.Contains('GIT_CONFIG_NOSYSTEM', [StringComparison]::Ordinal) -or
        -not $ProtocolModuleSource.Contains('EDGE_AUTHORITY_COORDINATOR_PARENT_BINDING_BASE64', [StringComparison]::Ordinal) -or
        -not $ProtocolModuleSource.Contains('EDGE_AUTHORITY_CHILD_BINDING_BASE64', [StringComparison]::Ordinal) -or
        -not $ProtocolModuleSource.Contains('Initialize-EdgeAuthorityGitChildEnvironment', [StringComparison]::Ordinal) -or
        -not $ProtocolModuleSource.Contains('Assert-EdgeExactCanonicalMarker', [StringComparison]::Ordinal) -or
        -not $ProtocolModuleSource.Contains('$capture = Wait-EdgeBoundedCaptureTasks $stdoutTask $stderrTask $deadline', [StringComparison]::Ordinal) -or
        -not $GeneratorSource.Contains("'diff', '--no-ext-diff'", [StringComparison]::Ordinal) -or
        -not $GeneratorSource.Contains('$authorityChildEnvironmentBound = [bool](Initialize-EdgeAuthorityGitChildEnvironment)', [StringComparison]::Ordinal) -or
        -not $GeneratorSource.Contains("if (-not `$authorityChildEnvironmentBound) {`n    Assert-EdgeAuthorityGitEnvironment`n}", [StringComparison]::Ordinal) -or
        -not $ValidatorSource.Contains('$authorityChildEnvironmentBound = [bool](Initialize-EdgeAuthorityGitChildEnvironment)', [StringComparison]::Ordinal) -or
        -not $ValidatorSource.Contains("if (-not `$authorityChildEnvironmentBound) {`n    Assert-EdgeAuthorityGitEnvironment`n}", [StringComparison]::Ordinal) -or
        -not $ValidatorSource.Contains('authority validator/fast mode requires the exact parent-controlled child binding', [StringComparison]::Ordinal) -or
        -not $ValidatorSource.Contains('msbuildCompileSourceCount = @($validatorCompileSourcePaths).Count', [StringComparison]::Ordinal) -or
        -not $BehaviorSource.Contains('if (-not [bool](Initialize-EdgeAuthorityGitChildEnvironment))', [StringComparison]::Ordinal) -or
        -not $RequiredXunitSource.Contains(
            'RunPowerShell(root, "scripts/tests/Test-EdgePluginContractStaticGuard.ps1")',
            [StringComparison]::Ordinal) -or
        -not $RequiredXunitSource.Contains(
            'RunPowerShell(root, "scripts/tests/Invoke-EdgePluginContractDevelopmentValidation.ps1")',
            [StringComparison]::Ordinal) -or
        -not $RequiredXunitSource.Contains(
            'JsonDocument.Parse(staticGuardResult.Output.Trim())',
            [StringComparison]::Ordinal) -or
        -not $RequiredXunitSource.Contains(
            'JsonDocument.Parse(result.Output.Trim())',
            [StringComparison]::Ordinal) -or
        -not $RequiredXunitSource.Contains(
            'value.GetProperty("mutationTotal").GetInt32()',
            [StringComparison]::Ordinal) -or
        -not $RequiredXunitSource.Contains(
            'value.GetProperty("deterministicMutationPassed").GetInt32()',
            [StringComparison]::Ordinal) -or
        -not $RequiredXunitSource.Contains(
            'value.GetProperty("deterministicMutationTotal").GetInt32()',
            [StringComparison]::Ordinal) -or
        -not $RequiredXunitSource.Contains(
            'value.GetProperty("deterministicMutationInventorySha256").GetString()',
            [StringComparison]::Ordinal) -or
        $RequiredXunitSource.Contains(
            'RunPowerShell(root, "scripts/tests/Test-EdgePluginContractLedgerBehavior.ps1")',
            [StringComparison]::Ordinal)) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 production coordinator lost its fixed executable/scripts/arguments.'
    }
    $serialCoordinatorNeedles = [string[]]@(
        '$deadline = [DateTimeOffset]::UtcNow.AddSeconds([int]$request.timeoutSeconds)',
        '$authorityChild = Start-AuthorityChild -WorkingDirectory $validatorWorktree -Arguments $authorityArguments -Name ''authority''',
        'Wait-AuthorityChildExit -Child $authorityChild -DeadlineUtc $deadline',
        '$authorityLog = Complete-AuthorityChild -Child $authorityChild -RunRoot $runRoot',
        '$replayChild = Start-AuthorityChild -WorkingDirectory $replayWorktree -Arguments $replayArguments.ToArray() -Name ''replay''',
        'Wait-AuthorityChildExit -Child $replayChild -DeadlineUtc $deadline',
        '$replayLog = Complete-AuthorityChild -Child $replayChild -RunRoot $runRoot'
    )
    $serialCoordinatorOffsets = [Collections.Generic.List[int]]::new()
    foreach ($needle in $serialCoordinatorNeedles) {
        if ([Text.RegularExpressions.Regex]::Matches(
                $CoordinatorSource, [Text.RegularExpressions.Regex]::Escape($needle),
                [Text.RegularExpressions.RegexOptions]::CultureInvariant).Count -ne 1) {
            throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 coordinator authority/replay serial statement inventory changed.'
        }
        $serialCoordinatorOffsets.Add($CoordinatorSource.IndexOf($needle, [StringComparison]::Ordinal))
    }
    for ($index = 1; $index -lt $serialCoordinatorOffsets.Count; $index++) {
        if ($serialCoordinatorOffsets[$index - 1] -ge $serialCoordinatorOffsets[$index]) {
            throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 coordinator must complete authority before starting replay under one deadline.'
        }
    }
    if ([Text.RegularExpressions.Regex]::Matches(
            $CoordinatorSource, 'function\s+Wait-AuthorityChildExit\b',
            [Text.RegularExpressions.RegexOptions]::CultureInvariant).Count -ne 1) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 coordinator must own one bounded serial child wait helper.'
    }
    $staticRunnerCallOffset = $RequiredXunitSource.IndexOf(
        'RunPowerShell(root, "scripts/tests/Test-EdgePluginContractStaticGuard.ps1")',
        [StringComparison]::Ordinal)
    $developmentCallOffset = $RequiredXunitSource.IndexOf(
        'RunPowerShell(root, "scripts/tests/Invoke-EdgePluginContractDevelopmentValidation.ps1")',
        [StringComparison]::Ordinal)
    if ($staticRunnerCallOffset -lt 0 -or $developmentCallOffset -lt 0 -or
        $staticRunnerCallOffset -ge $developmentCallOffset) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 required xUnit does not execute the static owner before development side effects.'
    }
    if ($CoordinatorSource.Contains('$coordinatorGitCommand = @(Get-Command git', [StringComparison]::Ordinal)) {
        throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 coordinator selected Git from its mutable startup PATH.'
    }
    foreach ($forbidden in @(
            'Invoke-EdgePluginContractAuthorityFixtureChild.ps1',
            'EDGE_AUTHORITY_PROTOCOL_FIXTURE', 'FixtureMode', 'CommandOverride',
            'ChildExecutableOverride', 'ChildScriptOverride')) {
        if ($CoordinatorSource.Contains($forbidden, [StringComparison]::Ordinal) -or
            $RequiredWrapperSource.Contains($forbidden, [StringComparison]::Ordinal)) {
            throw 'EDGE-SPLIT-AUTHORITY-STATIC-001 production coordinator/required wrapper exposes a fixture or child-command selector.'
        }
    }
}

function Assert-EdgePluginContractStaticGuard {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory, ParameterSetName = 'Repository')][string]$RepositoryRoot,
        [Parameter(Mandatory, ParameterSetName = 'Sources')][string]$CoordinatorSource,
        [Parameter(Mandatory, ParameterSetName = 'Sources')][string]$DevelopmentSource,
        [Parameter(Mandatory, ParameterSetName = 'Sources')][string]$FormalSource,
        [Parameter(Mandatory, ParameterSetName = 'Sources')][string]$ProtocolModuleSource,
        [Parameter(Mandatory, ParameterSetName = 'Sources')][string]$GeneratorSource,
        [Parameter(Mandatory, ParameterSetName = 'Sources')][string]$RequiredWrapperSource,
        [Parameter(Mandatory, ParameterSetName = 'Sources')][string]$ValidatorSource,
        [Parameter(Mandatory, ParameterSetName = 'Sources')][string]$BehaviorSource,
        [Parameter(Mandatory, ParameterSetName = 'Sources')][string]$RequiredXunitSource,
        [Parameter(Mandatory, ParameterSetName = 'Sources')][string]$DeterministicTargetsSource,
        [Parameter(Mandatory, ParameterSetName = 'Sources')][string]$MutationRunnerSource,
        [Parameter(Mandatory, ParameterSetName = 'MutationTarget')]
        [ValidateSet('development', 'protocolModule')][string]$MutationTarget,
        [Parameter(Mandatory, ParameterSetName = 'MutationTarget')]
        [string]$MutationName,
        [Parameter(Mandatory, ParameterSetName = 'MutationTarget')]
        [string]$TargetOwner,
        [Parameter(Mandatory, ParameterSetName = 'MutationTarget')]
        [string]$ExpectedShape,
        [Parameter(Mandatory, ParameterSetName = 'MutationTarget')]
        [string]$MutationSource,
        [switch]$PassThru
    )

    if ($PSCmdlet.ParameterSetName -ceq 'MutationTarget') {
        return Assert-EdgePluginContractMutationTargetGuard `
            $MutationTarget $MutationName $TargetOwner $ExpectedShape $MutationSource
    }
    elseif ($PSCmdlet.ParameterSetName -ceq 'Repository') {
        $root = [IO.Path]::GetFullPath($RepositoryRoot)
        $testsRoot = Join-Path $root 'scripts/tests'
        $CoordinatorSource = Get-Content -LiteralPath (
            Join-Path $testsRoot 'Invoke-EdgePluginContractAuthorityCoordinator.ps1') -Raw
        $DevelopmentSource = Get-Content -LiteralPath (
            Join-Path $testsRoot 'Invoke-EdgePluginContractDevelopmentValidation.ps1') -Raw
        $FormalSource = Get-Content -LiteralPath (
            Join-Path $testsRoot 'Invoke-EdgePluginContractFormalValidation.ps1') -Raw
        $ProtocolModuleSource = Get-Content -LiteralPath (
            Join-Path $testsRoot 'EdgePluginContractLedger.Protocol.psm1') -Raw
        $GeneratorSource = Get-Content -LiteralPath (
            Join-Path $root 'eng/Generate-EdgePluginContractLedger.ps1') -Raw
        $RequiredWrapperSource = Get-Content -LiteralPath (
            Join-Path $testsRoot 'Invoke-EdgeRequiredTests.ps1') -Raw
        $ValidatorSource = Get-Content -LiteralPath (
            Join-Path $testsRoot 'Test-EdgePluginContractLedger.ps1') -Raw
        $BehaviorSource = Get-Content -LiteralPath (
            Join-Path $testsRoot 'Test-EdgePluginContractLedgerBehavior.ps1') -Raw
        $RequiredXunitSource = Get-Content -LiteralPath (
            Join-Path $root 'src/Tests/IIoT.Edge.Architecture.Tests/EdgePluginContractLedgerTests.cs') -Raw
        $DeterministicTargetsSource = Get-Content -LiteralPath (
            Join-Path $root 'eng/EdgePluginContractDeterministicBuild.targets') -Raw
        $MutationRunnerSource = Get-Content -LiteralPath (
            Join-Path $testsRoot 'Test-EdgePluginContractStaticGuard.ps1') -Raw
    }

    Assert-ProductionCoordinatorStaticGuard `
        $CoordinatorSource $DevelopmentSource $FormalSource $ProtocolModuleSource $GeneratorSource `
        $RequiredWrapperSource $ValidatorSource $BehaviorSource $RequiredXunitSource `
        $DeterministicTargetsSource $MutationRunnerSource `
        -SkipFormalSourceDigest:($PSCmdlet.ParameterSetName -ceq 'Sources')

    if ($PassThru) {
        $sourceRows = [ordered]@{
            coordinator = $CoordinatorSource
            development = $DevelopmentSource
            formal = $FormalSource
            protocolModule = $ProtocolModuleSource
            generator = $GeneratorSource
            requiredWrapper = $RequiredWrapperSource
            validator = $ValidatorSource
            behavior = $BehaviorSource
            requiredXunit = $RequiredXunitSource
            deterministicTargets = $DeterministicTargetsSource
            mutationRunner = $MutationRunnerSource
        }
        $digests = [ordered]@{}
        foreach ($name in $sourceRows.Keys) {
            $bytes = [Text.UTF8Encoding]::new($false).GetBytes([string]$sourceRows[$name])
            $digests[$name] = [Convert]::ToHexString(
                [Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
        }
        return [pscustomobject][ordered]@{
            schemaVersion = 1
            owner = 'scripts/tests/EdgePluginContractStaticGuard.psm1'
            scope = 'production'
            passed = $true
            sourceCount = 11
            sourceDigests = [pscustomobject]$digests
        }
    }
}

Export-ModuleMember -Function Assert-EdgePluginContractStaticGuard
