param(
    # Volitelné: explicitní cesta k .sln
    [string]$SolutionFile = $null,
    # Volitelné: vlastní blacklist složek / souborů
    [string[]]$ExtraExcludeDirs = @('bin', 'obj', '.vs', '.git'),
    [string[]]$ExtraExcludeFiles = @('.gitignore')
)

$ErrorActionPreference = 'Stop'

function Resolve-SolutionFile {
    param([string]$Hint)
    if ($Hint -and (Test-Path $Hint)) { return (Resolve-Path $Hint).Path }

    # Hledej .sln v adresáři skriptu a pak o patro výš
    $scriptDir = if ($PSScriptRoot) { $PSScriptRoot }
                 elseif ($MyInvocation.MyCommand.Path) { Split-Path -Parent $MyInvocation.MyCommand.Path }
                 else { (Get-Location).Path }

    $candidates = @()
    $candidates += Get-ChildItem -Path $scriptDir -Filter *.sln -File -ErrorAction SilentlyContinue
    if (-not $candidates) {
        $candidates += Get-ChildItem -Path (Split-Path $scriptDir -Parent) -Filter *.sln -File -ErrorAction SilentlyContinue
    }
    if (-not $candidates) {
        throw "Nenašel jsem žádný .sln soubor. Pusť skript z rootu solution nebo dej parametr -SolutionFile."
    }
    if ($candidates.Count -gt 1) {
        Write-Host "Nalezeno více .sln, beru první: $($candidates[0].FullName)"
    }
    return $candidates[0].FullName
}


# 1) Najdi .sln a zjisti root
$SolutionPath = Resolve-SolutionFile -Hint $SolutionFile
$SolutionDir  = Split-Path -Parent $SolutionPath
$SolutionName = [System.IO.Path]::GetFileNameWithoutExtension($SolutionPath)

# 2) Definuj blacklist
$ExcludeDirs = @(
    '.git', '.vs', '.idea', '.vscode',
    'bin', 'obj', 'packages', 'TestResults', 'artifacts', 'publish', 'dist', 'out',
    'node_modules', '.terraform', '.angular', '.cache'
) + $ExtraExcludeDirs

$ExcludeFiles = @('*.user','*.suo','*.cache','*.log','*.tmp','*.lock','*.pdb','*.mdb','*.zip') + $ExtraExcludeFiles

# 3) Cesty pro staging a výsledný zip
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$ExportDir = Join-Path $SolutionDir 'export'
$null = New-Item -ItemType Directory -Path $ExportDir -Force | Out-Null

$Staging = Join-Path ([System.IO.Path]::GetTempPath()) ("export-" + [System.Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $Staging -Force | Out-Null

$ZipPath = Join-Path $ExportDir ("$SolutionName-$timestamp.zip")

Write-Host "Solution: $SolutionPath"
Write-Host "Staging:  $Staging"
Write-Host "ZIP:      $ZipPath"
Write-Host ""

# 4) Zkopíruj čistý strom pomocí ROBOCOPY (rychlé, spolehlivé)
# Přepínače udržuj v samostatném poli
$rcSwitches = @('/MIR','/NFL','/NDL','/NJH','/NJS','/NP','/MT:8')

if ($ExcludeDirs -and $ExcludeDirs.Count)  { $rcSwitches += @('/XD') + $ExcludeDirs }
if ($ExcludeFiles -and $ExcludeFiles.Count){ $rcSwitches += @('/XF') + $ExcludeFiles }

# PRO DEBUG: ukaž si přesně, co voláme
Write-Host "ROBOCOPY CMD:"
Write-Host "robocopy `"$SolutionDir`" `"$Staging`" $($rcSwitches -join ' ')"
Write-Host ""

# Volání robocopy s jistým pořadím (source, dest, switches)
$null = & robocopy "$SolutionDir" "$Staging" $rcSwitches
$code = $LASTEXITCODE
if ($code -ge 8) {
  Remove-Item -Recurse -Force $Staging -ErrorAction SilentlyContinue
  throw "ROBOCOPY selhalo s kódem $code."
}

# 5) Zabal do ZIP
if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
Write-Host "Pakuji ZIP…"
Compress-Archive -Path (Join-Path $Staging '*') -DestinationPath $ZipPath -CompressionLevel Optimal -Force

# 6) Úklid
Remove-Item -Recurse -Force $Staging -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "✅ Hotovo. Archiv:"
Write-Host $ZipPath
