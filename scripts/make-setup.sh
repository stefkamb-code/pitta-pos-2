#!/usr/bin/env bash
# Χτίζει self-contained single-file publish και ξαναφτιάχνει το PittaPOS-Setup.bat στην Επιφάνεια
# (ίδιο self-extracting bat wrapper — βλ. :PAYLOAD, base64-encoded zip του publish output).
# Δεύτερο βήμα του release flow, μετά το bump-version.sh, πριν το git commit.
set -e
cd "$(dirname "$0")/.."

PUBLISH_DIR="publish"
DESKTOP="/c/Users/stefa/Desktop"
VERSION="$(tr -d '[:space:]' < src/PittaPos.App/version.txt)"
BAT="$DESKTOP/PittaPOS2-Setup-v$VERSION.bat"
TMP_ZIP="$(cygpath -w "$(mktemp -u)").zip"
TMP_B64="$(mktemp)"

# Καθαρίζει παλιά setup.bat στην Επιφάνεια (και το παλιό όνομα χωρίς έκδοση) — ώστε να μένει πάντα
# μόνο ΕΝΑ, με το σωστό όνομα, χωρίς σύγχυση για το ποιο είναι το τρέχον.
rm -f "$DESKTOP"/PittaPOS2-Setup-v*.bat

echo "Publishing (self-contained, single-file)..."
dotnet publish src/PittaPos.App/PittaPos.App.csproj -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "$PUBLISH_DIR"

echo "Zipping publish output..."
powershell -NoProfile -Command "Compress-Archive -Path '$(cygpath -w "$PUBLISH_DIR")\*' -DestinationPath '$TMP_ZIP' -Force"

echo "Base64-encoding..."
powershell -NoProfile -Command "[Convert]::ToBase64String([IO.File]::ReadAllBytes('$TMP_ZIP')) | Set-Content -Encoding ascii -NoNewline '$(cygpath -w "$TMP_B64")'"

echo "Writing $BAT ..."
# UTF-8 BOM + CRLF wrapper, byte-identical header to the existing installer (πέρα από το version label).
printf '\xEF\xBB\xBF' > "$BAT"
sed "s/{{VERSION}}/$VERSION/" <<'HEADER' | sed 's/$/\r/' >> "$BAT"
@echo off
chcp 65001 >nul
title Pitta POS 2 - Εγκατάσταση (v{{VERSION}})
echo.
echo   Pitta POS 2 - Εγκατάσταση (v{{VERSION}})
echo   ------------------------
echo.
echo Κλείσιμο του προγράμματος αν είναι ανοιχτό...
taskkill /IM "PittaPos2.App.exe" /F >nul 2>&1
echo Προετοιμασία αρχείων, περίμενε λίγο...
powershell -NoProfile -ExecutionPolicy Bypass -Command "& { $ErrorActionPreference='Stop'; try { Get-Process -Name 'PittaPos2.App' -ErrorAction SilentlyContinue | Stop-Process -Force; $dest='C:\PittaPOS2'; $exe = Join-Path $dest 'PittaPos2.App.exe'; for ($i = 0; $i -lt 20; $i++) { if (-not (Test-Path $exe)) { break }; try { $fs = [System.IO.File]::Open($exe, 'Open', 'ReadWrite', 'None'); $fs.Close(); break } catch { Start-Sleep -Milliseconds 300 } }; $lines = Get-Content -LiteralPath '%~f0'; $startIdx = ($lines | Select-String -Pattern '^:PAYLOAD$' | Select-Object -First 1).LineNumber; $b64 = ($lines[$startIdx..($lines.Count-1)] -join ''); $bytes = [System.Convert]::FromBase64String($b64); $zip = Join-Path $env:TEMP 'pittapos2-install.zip'; [System.IO.File]::WriteAllBytes($zip, $bytes); if (-not (Test-Path $dest)) { New-Item -ItemType Directory -Path $dest | Out-Null }; Expand-Archive -Path $zip -DestinationPath $dest -Force; Remove-Item $zip -Force; $s = (New-Object -ComObject WScript.Shell).CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) 'Pitta POS 2.lnk')); $s.TargetPath = $exe; $s.WorkingDirectory = $dest; $s.IconLocation = $exe; $s.Save(); Write-Host 'OK' } catch { Write-Host ('SFALMA: ' + $_.Exception.Message); exit 1 } }"
if errorlevel 1 (
  echo.
  echo   ΣΦΑΛΜΑ — η εγκατάσταση ΔΕΝ ολοκληρώθηκε. Δες το μήνυμα παραπάνω ^(π.χ. αρχείο κλειδωμένο,
  echo   κλείσε το πρόγραμμα χειροκίνητα και ξανατρέξε αυτό το .bat^).
  echo.
  pause
  exit /b 1
)
echo.
echo   Ολοκληρώθηκε! Βρες το εικονίδιο "Pitta POS 2" στην Επιφάνεια Εργασίας.
echo.
pause
exit /b
:PAYLOAD
HEADER
fold -w 100 "$TMP_B64" | sed 's/$/\r/' >> "$BAT"

rm -f "$TMP_ZIP" "$TMP_B64"
echo "Done: $BAT"
