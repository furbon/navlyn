Set-StrictMode -Version Latest

function Get-NavlynJsoncDocument {
    param([Parameter(Mandatory)][string]$Text)

    # System.Text.Json validates syntax, comments, escapes and trailing commas.
    $jsonBytes = [Text.UTF8Encoding]::new($false, $true).GetBytes($Text)
    $options = [System.Text.Json.JsonDocumentOptions]::new()
    $options.AllowTrailingCommas = $true
    $options.CommentHandling = [System.Text.Json.JsonCommentHandling]::Skip
    $document = [System.Text.Json.JsonDocument]::Parse([ReadOnlyMemory[byte]]::new($jsonBytes), $options)
    try {
        Assert-NavlynJsoncNoDuplicateKeys -Element $document.RootElement
    } catch {
        $document.Dispose()
        throw
    }

    $tokens = [Collections.Generic.List[object]]::new()
    $index = 0
    while ($index -lt $Text.Length) {
        $c = $Text[$index]
        if ([char]::IsWhiteSpace($c)) { $index++; continue }
        if ($c -eq '/' -and $index + 1 -lt $Text.Length -and $Text[$index + 1] -eq '/') {
            $index += 2
            while ($index -lt $Text.Length -and $Text[$index] -notin "`r", "`n") { $index++ }
            continue
        }
        if ($c -eq '/' -and $index + 1 -lt $Text.Length -and $Text[$index + 1] -eq '*') {
            $commentStart = $index
            $index += 2
            while ($index + 1 -lt $Text.Length -and !($Text[$index] -eq '*' -and $Text[$index + 1] -eq '/')) { $index++ }
            if ($index + 1 -ge $Text.Length) { $document.Dispose(); throw "Unterminated JSONC block comment at offset $commentStart." }
            $index += 2
            continue
        }
        $start = $index
        if ($c -eq '"') {
            $index++
            $escaped = $false
            while ($index -lt $Text.Length) {
                $current = $Text[$index]
                if ($escaped) { $escaped = $false; $index++; continue }
                if ($current -eq '\') { $escaped = $true; $index++; continue }
                if ($current -eq '"') { $index++; break }
                $index++
            }
            if ($index -gt $Text.Length -or $Text[$index - 1] -ne '"') { $document.Dispose(); throw "Unterminated JSONC string at offset $start." }
            $raw = $Text.Substring($start, $index - $start)
            $value = [System.Text.Json.JsonSerializer]::Deserialize[string]($raw)
            $tokens.Add([pscustomobject]@{ Kind='string'; Value=$value; Start=$start; End=$index })
            continue
        }
        $kind = switch ($c) {
            '{' { 'objectStart' }
            '}' { 'objectEnd' }
            '[' { 'arrayStart' }
            ']' { 'arrayEnd' }
            ':' { 'colon' }
            ',' { 'comma' }
            default { $null }
        }
        if ($kind) { $index++; $tokens.Add([pscustomobject]@{ Kind=$kind; Value=$null; Start=$start; End=$index }); continue }
        while ($index -lt $Text.Length -and ![char]::IsWhiteSpace($Text[$index]) -and $Text[$index] -notin '{','}','[',']',':',',','/','"') { $index++ }
        if ($index -eq $start) { $document.Dispose(); throw "Unexpected JSONC character at offset $index." }
        $tokens.Add([pscustomobject]@{ Kind='primitive'; Value=$Text.Substring($start, $index - $start); Start=$start; End=$index })
    }

    $cursor = 0
    $root = Read-NavlynJsoncNode -Tokens $tokens -Cursor ([ref]$cursor)
    if ($cursor -ne $tokens.Count) { $document.Dispose(); throw 'Unexpected tokens after JSONC root value.' }
    return [pscustomobject]@{ Document=$document; Root=$root; Tokens=$tokens }
}

function Assert-NavlynJsoncNoDuplicateKeys {
    param([System.Text.Json.JsonElement]$Element)
    if ($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Object) {
        $keys = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($property in $Element.EnumerateObject()) {
            if (!$keys.Add($property.Name)) { throw "Duplicate JSONC object key '$($property.Name)'." }
            Assert-NavlynJsoncNoDuplicateKeys -Element $property.Value
        }
    } elseif ($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Array) {
        foreach ($child in $Element.EnumerateArray()) { Assert-NavlynJsoncNoDuplicateKeys -Element $child }
    }
}

function Read-NavlynJsoncNode {
    param([Collections.Generic.List[object]]$Tokens, [ref]$Cursor)
    if ($Cursor.Value -ge $Tokens.Count) { throw 'Unexpected end of JSONC document.' }
    $token = $Tokens[$Cursor.Value]
    $Cursor.Value++
    if ($token.Kind -eq 'objectStart') {
        $node = [pscustomobject]@{ Kind='object'; Start=$token.Start; End=$null; Properties=[Collections.Generic.List[object]]::new() }
        while ($Cursor.Value -lt $Tokens.Count -and $Tokens[$Cursor.Value].Kind -ne 'objectEnd') {
            if ($Tokens[$Cursor.Value].Kind -eq 'comma') { $Cursor.Value++; continue }
            $key = $Tokens[$Cursor.Value]
            if ($key.Kind -ne 'string') { throw "Expected a quoted object key at offset $($key.Start)." }
            $Cursor.Value++
            if ($Cursor.Value -ge $Tokens.Count -or $Tokens[$Cursor.Value].Kind -ne 'colon') { throw "Expected ':' after object key at offset $($key.Start)." }
            $Cursor.Value++
            $value = Read-NavlynJsoncNode -Tokens $Tokens -Cursor $Cursor
            $node.Properties.Add([pscustomobject]@{ Name=$key.Value; PropertyStart=$key.Start; KeyEnd=$key.End; Value=$value })
            if ($Cursor.Value -lt $Tokens.Count -and $Tokens[$Cursor.Value].Kind -eq 'comma') { $Cursor.Value++ }
        }
        if ($Cursor.Value -ge $Tokens.Count) { throw "Unclosed JSONC object at offset $($token.Start)." }
        $close = $Tokens[$Cursor.Value]; $Cursor.Value++
        $node.End = $close.End
        return $node
    }
    if ($token.Kind -eq 'arrayStart') {
        $node = [pscustomobject]@{ Kind='array'; Start=$token.Start; End=$null; Items=[Collections.Generic.List[object]]::new() }
        while ($Cursor.Value -lt $Tokens.Count -and $Tokens[$Cursor.Value].Kind -ne 'arrayEnd') {
            if ($Tokens[$Cursor.Value].Kind -eq 'comma') { $Cursor.Value++; continue }
            $node.Items.Add((Read-NavlynJsoncNode -Tokens $Tokens -Cursor $Cursor))
            if ($Cursor.Value -lt $Tokens.Count -and $Tokens[$Cursor.Value].Kind -eq 'comma') { $Cursor.Value++ }
        }
        if ($Cursor.Value -ge $Tokens.Count) { throw "Unclosed JSONC array at offset $($token.Start)." }
        $close = $Tokens[$Cursor.Value]; $Cursor.Value++
        $node.End = $close.End
        return $node
    }
    if ($token.Kind -notin 'string','primitive') { throw "Unexpected JSONC token at offset $($token.Start)." }
    return [pscustomobject]@{ Kind='scalar'; Start=$token.Start; End=$token.End; Value=$token.Value }
}

function ConvertFrom-NavlynJsonc {
    param([Parameter(Mandatory)][string]$Text)
    $parsed = Get-NavlynJsoncDocument $Text
    try { return ConvertTo-NavlynHashtable -Element $parsed.Document.RootElement }
    finally { $parsed.Document.Dispose() }
}

function ConvertTo-NavlynHashtable {
    param([System.Text.Json.JsonElement]$Element)
    switch ($Element.ValueKind) {
        Object {
            $result = [Collections.Specialized.OrderedDictionary]::new([StringComparer]::Ordinal)
            foreach ($property in $Element.EnumerateObject()) { $result[$property.Name] = ConvertTo-NavlynHashtable -Element $property.Value }
            return $result
        }
        Array {
            $result = [Collections.Generic.List[object]]::new()
            foreach ($item in $Element.EnumerateArray()) { $result.Add((ConvertTo-NavlynHashtable -Element $item)) }
            return ,$result.ToArray()
        }
        String { return $Element.GetString() }
        True { return $true }
        False { return $false }
        Null { return $null }
        default { return ConvertFrom-Json -InputObject $Element.GetRawText() }
    }
}

function Get-NavlynJsoncPropertySpan {
    param([string]$Text, [string]$ObjectName, [string]$PropertyName)
    $parsed = Get-NavlynJsoncDocument $Text
    try {
        if ($parsed.Root.Kind -ne 'object') { return $null }
        $owner = @($parsed.Root.Properties | Where-Object Name -CEQ $ObjectName | Select-Object -First 1)
        if (!$owner -or $owner[0].Value.Kind -ne 'object') { return $null }
        $property = @($owner[0].Value.Properties | Where-Object Name -CEQ $PropertyName | Select-Object -First 1)
        if (!$property) { return $null }
        $value = $property[0].Value
        return [pscustomobject]@{ Start=$value.Start; Length=$value.End-$value.Start; PropertyStart=$property[0].PropertyStart; ObjectStart=$owner[0].Value.Start; ObjectEnd=$owner[0].Value.End }
    } finally { $parsed.Document.Dispose() }
}

function Get-NavlynJsoncRootProperty {
    param([string]$Text, [string]$Name)
    $parsed = Get-NavlynJsoncDocument $Text
    try {
        if ($parsed.Root.Kind -ne 'object') { throw 'JSONC root must be an object.' }
        $property = @($parsed.Root.Properties | Where-Object Name -CEQ $Name | Select-Object -First 1)
        if (!$property) { return $null }
        return $property[0]
    } finally { $parsed.Document.Dispose() }
}

function Get-NavlynJsoncDirectProperty {
    param([string]$Text, [string]$ObjectName, [string]$Name)
    $parsed = Get-NavlynJsoncDocument $Text
    try {
        if ($parsed.Root.Kind -ne 'object') { throw 'JSONC root must be an object.' }
        $parent = @($parsed.Root.Properties | Where-Object Name -CEQ $ObjectName | Select-Object -First 1)
        if (!$parent -or $parent[0].Value.Kind -ne 'object') { return $null }
        $property = @($parent[0].Value.Properties | Where-Object Name -CEQ $Name | Select-Object -First 1)
        if (!$property) { return $null }
        return $property[0]
    } finally { $parsed.Document.Dispose() }
}

function Add-NavlynJsoncProperty {
    param([string]$Text, [string]$ObjectName, [string]$PropertyName, [string]$ValueJson)
    $owner = Get-NavlynJsoncRootProperty $Text $ObjectName
    if (!$owner) { throw "Cannot locate JSONC object '$ObjectName'." }
    if ($owner.Value.Kind -ne 'object') { throw "JSONC property '$ObjectName' is not an object." }
    $newline = if ($Text.Contains("`r`n")) { "`r`n" } else { "`n" }
    $hasProperties = $owner.Value.Properties.Count -gt 0
    $hasTrailingComma = $false
    if ($hasProperties) {
        $lastEnd = $owner.Value.Properties[$owner.Value.Properties.Count - 1].Value.End
        $parsed = Get-NavlynJsoncDocument $Text
        try { $hasTrailingComma = @($parsed.Tokens | Where-Object { $_.Kind -eq 'comma' -and $_.Start -ge $lastEnd -and $_.End -lt $owner.Value.End } | Select-Object -First 1).Count -gt 0 }
        finally { $parsed.Document.Dispose() }
    }
    $separator = if ($hasProperties -and !$hasTrailingComma) { ',' } else { '' }
    return $Text.Insert($owner.Value.End - 1, $separator + $newline + "    `"$PropertyName`": $ValueJson" + $newline + '  ')
}

function Add-NavlynJsoncRootServers {
    param([string]$Text, [string]$EntryName, [string]$EntryJson)
    $parsed = Get-NavlynJsoncDocument $Text
    try {
        if ($parsed.Root.Kind -ne 'object') { throw 'JSONC root must be an object.' }
        $newline = if ($Text.Contains("`r`n")) { "`r`n" } else { "`n" }
        $close = $parsed.Root.End - 1
        $hasProperties = $parsed.Root.Properties.Count -gt 0
        $hasTrailingComma = $false
        if ($hasProperties) {
            $lastEnd = $parsed.Root.Properties[$parsed.Root.Properties.Count - 1].Value.End
            $hasTrailingComma = @($parsed.Tokens | Where-Object { $_.Kind -eq 'comma' -and $_.Start -ge $lastEnd -and $_.End -lt $parsed.Root.End } | Select-Object -First 1).Count -gt 0
        }
        $separator = if ($hasProperties -and !$hasTrailingComma) { ',' } else { '' }
        return $Text.Insert($close, $separator + $newline + "  `"servers`": {`"$EntryName`": $EntryJson}" + $newline)
    } finally { $parsed.Document.Dispose() }
}

function Set-NavlynJsoncServer {
    param([string]$Text, [string]$Name, [string]$EntryJson)
    $doc = ConvertFrom-NavlynJsonc $Text
    if ($doc -isnot [System.Collections.IDictionary]) { throw 'JSONC root must be an object.' }
    if (!$doc.Contains('servers')) { return Add-NavlynJsoncRootServers -Text $Text -EntryName $Name -EntryJson $EntryJson }
    if ($null -eq $doc['servers'] -or $doc['servers'] -isnot [System.Collections.IDictionary]) { throw 'Configuration servers property must be an object.' }
    $existing = Get-NavlynJsoncDirectProperty -Text $Text -ObjectName 'servers' -Name $Name
    if ($existing) {
        if ($existing.Value.Kind -ne 'object') { throw "servers.$Name is not an object." }
        return $Text.Substring(0, $existing.Value.Start) + $EntryJson + $Text.Substring($existing.Value.End)
    }
    return Add-NavlynJsoncProperty -Text $Text -ObjectName 'servers' -PropertyName $Name -ValueJson $EntryJson
}

function Remove-NavlynJsoncServer {
    param([string]$Text, [string]$Name)
    $parsed = Get-NavlynJsoncDocument $Text
    try {
        if ($parsed.Root.Kind -ne 'object') { throw 'JSONC root must be an object.' }
        $owner = @($parsed.Root.Properties | Where-Object Name -CEQ 'servers' | Select-Object -First 1)
        if (!$owner -or $owner[0].Value.Kind -ne 'object') { return $Text }
        $properties = @($owner[0].Value.Properties)
        $index = -1
        for ($i=0; $i -lt $properties.Count; $i++) { if ($properties[$i].Name -ceq $Name) { $index=$i; break } }
        if ($index -lt 0) { return $Text }
        $property = $properties[$index]
        $spans = [Collections.Generic.List[object]]::new()
        $spans.Add([pscustomobject]@{ Start=$property.PropertyStart; Length=$property.Value.End-$property.PropertyStart })
        $tokens = $parsed.Tokens
        $followingComma = @($tokens | Where-Object { $_.Kind -eq 'comma' -and $_.Start -ge $property.Value.End -and $_.End -le $owner[0].Value.End } | Select-Object -First 1)
        if ($index -lt $properties.Count - 1 -and $followingComma) { $spans.Add([pscustomobject]@{ Start=$followingComma[0].Start; Length=$followingComma[0].End-$followingComma[0].Start }) }
        elseif ($index -eq 0 -and $properties.Count -eq 1 -and $followingComma) { $spans.Add([pscustomobject]@{ Start=$followingComma[0].Start; Length=$followingComma[0].End-$followingComma[0].Start }) }
        elseif ($index -gt 0) {
            $priorEnd = $properties[$index - 1].Value.End
            $priorComma = @($tokens | Where-Object { $_.Kind -eq 'comma' -and $_.Start -ge $priorEnd -and $_.End -le $property.PropertyStart } | Select-Object -Last 1)
            if ($priorComma) { $spans.Add([pscustomobject]@{ Start=$priorComma[0].Start; Length=$priorComma[0].End-$priorComma[0].Start }) }
        }
        foreach ($span in @($spans | Sort-Object Start -Descending)) { $Text = $Text.Remove($span.Start, $span.Length) }
        return $Text
    } finally { $parsed.Document.Dispose() }
}
