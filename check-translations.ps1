param(
    [switch]$Sync,
    [string]$TranslationDirectory = (Join-Path $PSScriptRoot 'Translations'),
    [string]$SourceDirectory = (Join-Path $PSScriptRoot 'src')
)
$ErrorActionPreference = 'Stop'

function Resolve-Directory([string]$Path) {
    $resolved = Resolve-Path -LiteralPath $Path
    if ($resolved.Provider.Name -ne 'FileSystem' -or -not (Test-Path -LiteralPath $resolved.ProviderPath -PathType Container)) {
        throw "Expected an existing filesystem directory: $Path"
    }
    return $resolved.ProviderPath
}

function Read-Translation([string]$Path, [bool]$AllowNull, [Collections.Generic.List[string]]$Problems) {
    $document = $null
    try {
        $original = [IO.File]::ReadAllText($Path)
        $document = [System.Text.Json.JsonDocument]::Parse($original)
        if ($document.RootElement.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) {
            throw "Expected an object: $Path"
        }
        $values = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
        foreach ($property in $document.RootElement.EnumerateObject()) {
            if ($values.ContainsKey($property.Name)) {
                $Problems.Add("Duplicate translation key: $Path / $($property.Name)")
                continue
            }
            if ($property.Value.ValueKind -eq [System.Text.Json.JsonValueKind]::String) {
                $values.Add($property.Name, $property.Value.GetString())
            } elseif ($AllowNull -and $property.Value.ValueKind -eq [System.Text.Json.JsonValueKind]::Null) {
                $values.Add($property.Name, $null)
            } else {
                $Problems.Add("Expected $(if ($AllowNull) { 'a string or null' } else { 'a string' }): $Path / $($property.Name)")
                $values.Add($property.Name, $null)
            }
        }
        return [pscustomobject]@{ Path = $Path; Name = [IO.Path]::GetFileName($Path); Original = $original; Values = $values }
    }
    catch {
        $Problems.Add("Cannot read translation JSON: $Path / $($_.Exception.Message)")
        return $null
    }
    finally {
        if ($null -ne $document) { $document.Dispose() }
    }
}

function Stop-OnProblems([Collections.Generic.List[string]]$Problems) {
    if ($Problems.Count -gt 0) {
        throw ("Translation validation failed ($($Problems.Count) errors):" + [Environment]::NewLine + ($Problems -join [Environment]::NewLine))
    }
}

function Test-Culture($Translation, [Collections.Generic.List[string]]$Problems) {
    if (-not $Translation.Values.ContainsKey('meta.culture') -or $null -eq $Translation.Values['meta.culture']) { return }
    try {
        $null = [Globalization.CultureInfo]::GetCultureInfo($Translation.Values['meta.culture'])
    }
    catch {
        $Problems.Add("Invalid culture: $($Translation.Name) / meta.culture / $($Translation.Values['meta.culture'])")
    }
}

function Write-Translation($Translation) {
    $keys = [string[]]@($Translation.Values.Keys)
    [Array]::Sort($keys, [StringComparer]::Ordinal)
    $stream = [IO.MemoryStream]::new()
    $options = [System.Text.Json.JsonWriterOptions]::new()
    $options.Indented = $true
    $options.Encoder = [System.Text.Encodings.Web.JavaScriptEncoder]::UnsafeRelaxedJsonEscaping
    $writer = [System.Text.Json.Utf8JsonWriter]::new($stream, $options)
    $temporary = Join-Path ([IO.Path]::GetDirectoryName($Translation.Path)) ('.translation-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        $writer.WriteStartObject()
        foreach ($key in $keys) {
            if ($null -eq $Translation.Values[$key]) {
                $writer.WriteNull($key)
            } else {
                $writer.WriteString($key, $Translation.Values[$key])
            }
        }
        $writer.WriteEndObject()
        $writer.Flush()
        [IO.File]::WriteAllBytes($temporary, $stream.ToArray())
        if ([IO.File]::ReadAllText($Translation.Path) -cne $Translation.Original) {
            throw "Translation changed during synchronization; rerun without overwriting it: $($Translation.Path)"
        }
        [IO.File]::Replace($temporary, $Translation.Path, [System.Management.Automation.Language.NullString]::Value)
    }
    finally {
        $writer.Dispose()
        $stream.Dispose()
        if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) }
    }
}

$translationRoot = Resolve-Directory $TranslationDirectory
$sourceRoot = Resolve-Directory $SourceDirectory
$problems = [Collections.Generic.List[string]]::new()
$catalog = Read-Translation (Join-Path $translationRoot 'catalog.json') $false $problems
$fallbackName = if ($null -ne $catalog) { $catalog.Values['fallback'] } else { $null }
if (-not $fallbackName -or [IO.Path]::GetFileName($fallbackName) -cne $fallbackName -or $fallbackName -in @('.', '..', 'catalog')) {
    $problems.Add('catalog.json / fallback must name a fallback dictionary in this directory.')
}

$translations = [Collections.Generic.List[object]]::new()
$fallbackTranslation = $null
foreach ($file in Get-ChildItem -LiteralPath $translationRoot -Filter '*.json' -File | Sort-Object Name) {
    if ($file.Name -eq 'catalog.json') { continue }
    $isFallback = $file.BaseName -ceq $fallbackName
    $translation = Read-Translation $file.FullName (-not $isFallback) $problems
    if ($null -eq $translation) { continue }
    $translations.Add($translation)
    Test-Culture $translation $problems
    if ($isFallback) { $fallbackTranslation = $translation }
}
if ($null -eq $fallbackTranslation) {
    $problems.Add("Fallback dictionary not found or invalid: catalog.json / fallback / $fallbackName")
} else {
    if (-not $fallbackTranslation.Values.ContainsKey('meta.culture')) {
        $problems.Add("Missing translation key: $($fallbackTranslation.Name) / meta.culture")
    }
}
Stop-OnProblems $problems
$fallback = $fallbackTranslation.Values
$fallbackKeys = [string[]]@($fallback.Keys)
[Array]::Sort($fallbackKeys, [StringComparer]::Ordinal)

if ($Sync) {
    foreach ($translation in $translations) {
        if ($translation -eq $fallbackTranslation) { continue }
        $added = [Collections.Generic.List[string]]::new()
        foreach ($key in $fallbackKeys) {
            if (-not $translation.Values.ContainsKey($key)) {
                $translation.Values.Add($key, $null)
                $added.Add($key)
            }
        }
        if ($added.Count -eq 0) { continue }
        Write-Translation $translation
        foreach ($key in $added) { Write-Output "Added pending translation: $($translation.Name) / $key" }
    }
}

foreach ($translation in $translations) {
    $values = $translation.Values
    foreach ($key in $fallbackKeys) {
        if (-not $values.ContainsKey($key)) {
            $problems.Add("Missing translation key: $($translation.Name) / $key")
        }
    }
    foreach ($key in $values.Keys) {
        if (-not $fallback.ContainsKey($key)) {
            $problems.Add("Unknown translation key: $($translation.Name) / $key")
        }
        if ($null -eq $values[$key]) {
            $problems.Add("Pending translation: $($translation.Name) / $key")
            continue
        }
        if (-not $fallback.ContainsKey($key)) { continue }
        $expected = @([regex]::Matches($fallback[$key], '(?<!\{)\{(\d+)(?:[^{}]*)\}(?!\})') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
        $actual = @([regex]::Matches($values[$key], '(?<!\{)\{(\d+)(?:[^{}]*)\}(?!\})') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
        if (($expected -join ',') -cne ($actual -join ',')) { $problems.Add("Placeholder mismatch: $($translation.Name) / $key") }
    }
}
$keyLiteral = '"((?:\\.|[^"\\])*)"'
foreach ($file in Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' -File) {
    $source = [IO.File]::ReadAllText($file.FullName)
    foreach ($match in [regex]::Matches($source, '\b(?:BoomboxText\.(?:Get|Format|Description)|Notify|SetStatus|new\s+BoomboxMessage)\(\s*' + $keyLiteral)) {
        if (-not $fallback.ContainsKey($match.Groups[1].Value)) { $problems.Add("Missing translation key: $($file.Name) / $($match.Groups[1].Value)") }
    }
}
Stop-OnProblems $problems
Write-Output 'Translation dictionaries are complete and valid.'
