[CmdletBinding()]
param()

function Get-NavlynPreferredTargetFramework {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectPath,

        [string]$PreferredTargetFramework = 'net10.0'
    )

    [xml]$projectXml = Get-Content -Raw -LiteralPath $ProjectPath
    $targetFrameworks = [System.Collections.Generic.List[string]]::new()

    foreach ($node in @($projectXml.SelectNodes('//TargetFramework'))) {
        $targetFramework = [string]$node.InnerText
        if (![string]::IsNullOrWhiteSpace($targetFramework)) {
            $targetFrameworks.Add($targetFramework.Trim())
        }
    }

    foreach ($node in @($projectXml.SelectNodes('//TargetFrameworks'))) {
        $multiTargetFrameworks = [string]$node.InnerText
        if (![string]::IsNullOrWhiteSpace($multiTargetFrameworks)) {
            foreach ($target in $multiTargetFrameworks.Split(';')) {
                if (![string]::IsNullOrWhiteSpace($target)) {
                    $targetFrameworks.Add($target.Trim())
                }
            }
        }
    }

    $uniqueTargetFrameworks = @($targetFrameworks | Select-Object -Unique)
    if ($uniqueTargetFrameworks.Count -eq 0) {
        throw "No TargetFramework or TargetFrameworks found in $ProjectPath."
    }

    if ($uniqueTargetFrameworks -contains $PreferredTargetFramework) {
        return $PreferredTargetFramework
    }

    if ($uniqueTargetFrameworks -contains 'net10.0') {
        return 'net10.0'
    }

    return $uniqueTargetFrameworks[0]
}
