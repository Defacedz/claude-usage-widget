# ============================================================
#  Installs ClaudeWidget.exe (uiAccess) - RUN AS ADMINISTRATOR
#  1. builds ClaudeWidget.cs with the csc.exe shipped in Windows
#  2. creates/reuses a local certificate and signs the exe
#  3. trusts that certificate on this machine (required by uiAccess)
#  4. installs into Program Files and starts it
#
#  Keep this file ASCII-only: PowerShell 5.1 reads a BOM-less .ps1 as
#  ANSI, so an accented character here would corrupt the script.
# ============================================================
$ErrorActionPreference = 'Stop'
function Fail($msg) { Write-Host "`n[ERROR] $msg" -ForegroundColor Red; Read-Host 'Press Enter to close'; exit 1 }

try {
    $here = Split-Path -Parent $MyInvocation.MyCommand.Path
    $src  = Join-Path $here 'ClaudeWidget.cs'
    $man  = Join-Path $here 'app.manifest'
    if (-not (Test-Path $src) -or -not (Test-Path $man)) { Fail 'ClaudeWidget.cs or app.manifest not found next to this script.' }

    # --- admin? ---
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    if (-not (New-Object Security.Principal.WindowsPrincipal($id)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        Fail 'This script must run as administrator (use Installer.bat).'
    }

    # --- stop any running instance ---
    Write-Host '1/6 Stopping running instances...'
    Get-Process -Name 'ClaudeWidget' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Get-Process -Name 'ClaudeWidgetFeed' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500

    # --- build ---
    Write-Host '2/6 Building...'
    $fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
    if (-not (Test-Path (Join-Path $fw 'csc.exe'))) { $fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319' }
    $csc = Join-Path $fw 'csc.exe'
    if (-not (Test-Path $csc)) { Fail 'C# compiler (.NET Framework 4) not found.' }
    $exe = Join-Path $env:TEMP 'ClaudeWidget.exe'
    Remove-Item $exe -Force -ErrorAction SilentlyContinue
    $wpf = Join-Path $fw 'WPF'
    # /codepage:65001 is a safety net: the source is UTF-8 with a BOM, but an
    # editor that strips the BOM would otherwise mangle the translated strings.
    & $csc /nologo /target:winexe /out:$exe /win32manifest:$man /codepage:65001 `
        /r:System.dll /r:System.Core.dll /r:System.Xaml.dll `
        /r:System.Runtime.Serialization.dll /r:Microsoft.CSharp.dll `
        /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll `
        /r:"$wpf\PresentationFramework.dll" /r:"$wpf\PresentationCore.dll" /r:"$wpf\WindowsBase.dll" `
        $src
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $exe)) { Fail 'Build failed (see messages above).' }

    # Second build, same source, WITHOUT the uiAccess manifest: the feed
    # helper Claude Code spawns as its statusline command. A uiAccess process
    # asks for a higher integrity level that a statusline spawn neither needs
    # nor reliably gets, so the helper must not carry it. No signing needed.
    $feedExe = Join-Path $env:TEMP 'ClaudeWidgetFeed.exe'
    Remove-Item $feedExe -Force -ErrorAction SilentlyContinue
    & $csc /nologo /target:winexe /out:$feedExe /codepage:65001 `
        /r:System.dll /r:System.Core.dll /r:System.Xaml.dll `
        /r:System.Runtime.Serialization.dll /r:Microsoft.CSharp.dll `
        /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll `
        /r:"$wpf\PresentationFramework.dll" /r:"$wpf\PresentationCore.dll" /r:"$wpf\WindowsBase.dll" `
        $src
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $feedExe)) { Fail 'Feed helper build failed (see messages above).' }

    # --- certificate ---
    Write-Host '3/6 Local signing certificate...'
    $subject = 'CN=ClaudeWidget Local'
    $cert = Get-ChildItem Cert:\LocalMachine\My -ErrorAction SilentlyContinue |
            Where-Object { $_.Subject -eq $subject -and $_.HasPrivateKey } | Select-Object -First 1
    if (-not $cert) {
        $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $subject `
                -CertStoreLocation 'Cert:\LocalMachine\My' -NotAfter (Get-Date).AddYears(10)
    }

    Write-Host '4/6 Trusting the certificate...'
    $cer = Join-Path $env:TEMP 'ClaudeWidgetLocal.cer'
    Export-Certificate -Cert $cert -FilePath $cer | Out-Null
    Import-Certificate -FilePath $cer -CertStoreLocation 'Cert:\LocalMachine\Root' | Out-Null
    Import-Certificate -FilePath $cer -CertStoreLocation 'Cert:\LocalMachine\TrustedPublisher' | Out-Null
    Remove-Item $cer -Force -ErrorAction SilentlyContinue

    Write-Host '5/6 Signing and installing...'
    $sig = Set-AuthenticodeSignature -FilePath $exe -Certificate $cert -HashAlgorithm SHA256
    if ($sig.Status -ne 'Valid') { Fail ('Invalid signature: ' + $sig.StatusMessage) }
    $destDir = Join-Path $env:ProgramFiles 'ClaudeWidget'
    New-Item -ItemType Directory -Path $destDir -Force | Out-Null
    Copy-Item $exe (Join-Path $destDir 'ClaudeWidget.exe') -Force
    Copy-Item $feedExe (Join-Path $destDir 'ClaudeWidgetFeed.exe') -Force
    Remove-Item $exe -Force -ErrorAction SilentlyContinue
    Remove-Item $feedExe -Force -ErrorAction SilentlyContinue

    Write-Host '6/6 Starting...'
    # Launched straight from this elevated shell, the widget would inherit
    # administrator rights it never needs - and would keep them until the next
    # sign-in. Handing the path to Explorer starts it with the normal user
    # token instead, exactly as the Startup shortcut does. If the shell is not
    # running (replaced, or a bare session), fall back to starting it here.
    $widget = Join-Path $destDir 'ClaudeWidget.exe'
    $started = $false
    if (Get-Process -Name 'explorer' -ErrorAction SilentlyContinue) {
        try {
            Start-Process 'explorer.exe' -ArgumentList ('"' + $widget + '"')
            for ($i = 0; $i -lt 20 -and -not $started; $i++) {
                Start-Sleep -Milliseconds 250
                $started = [bool](Get-Process -Name 'ClaudeWidget' -ErrorAction SilentlyContinue)
            }
        } catch { $started = $false }
    }
    if (-not $started) { Start-Process $widget }

    Write-Host "`n[OK] Installed and started from $destDir" -ForegroundColor Green
    Write-Host 'Right-click the widget for language, opacity, autostart and quit.'
    Read-Host 'Press Enter to close'
} catch {
    Fail $_.Exception.Message
}
