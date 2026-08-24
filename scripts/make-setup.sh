#!/usr/bin/env bash
# Χτίζει self-contained single-file publish και ξαναφτιάχνει το PittaPOS-Setup.bat στην Επιφάνεια
# (ίδιο self-extracting bat wrapper — βλ. :PAYLOAD, base64-encoded zip του publish output).
# Δεύτερο βήμα του release flow, μετά το bump-version.sh, πριν το git commit.
#
# ΔΥΟ συντομεύσεις φτιάχνει πλέον: «Pitta POS 2» (το ταμείο) και «Ζωντανές Παραγγελίες» (ίδιο exe με
# όρισμα --live, δικό του εικονίδιο live-orders.ico με τον ντελιβερά). Το ελληνικό όνομα της δεύτερης
# ΔΕΝ γράφεται ως κείμενο μέσα στο .bat: περνάει από cmd -> powershell και βγαίνει αλλοιωμένο (μετρημένο,
# το αρχείο δεν δημιουργείται καν). Χτίζεται από code points με [char], που είναι καθαρό ASCII στο .bat.
#
#   ./scripts/make-setup.sh                     -> σκέτο setup, ΔΕΝ αγγίζει τον κατάλογο (κανονική έκδοση)
#   ./scripts/make-setup.sh /c/.../menu.json    -> κουβαλάει ΚΑΙ κατάλογο, τον βάζει ΜΙΑ φορά
#
# Το δεύτερο είναι η εξαίρεση, όχι ο κανόνας: ένα setup ξανατρέχει (νέο PC, «ας το ξαναβάλω») και ένα
# setup που κουβαλά μενού θα έσβηνε τότε ό,τι έχει αλλάξει το μαγαζί στο μεταξύ. Γι' αυτό ο κατάλογος
# μπαίνει μόνο αν δεν έχει ήδη μπει (σημάδι menu-version.txt) και πάντα με αντίγραφο του παλιού.
set -e
cd "$(dirname "$0")/.."

PUBLISH_DIR="publish"
DESKTOP="/c/Users/stefa/Desktop"
VERSION="$(tr -d '[:space:]' < src/PittaPos.App/version.txt)"
BAT="$DESKTOP/PittaPOS2-Setup-v$VERSION.bat"
TMP_ZIP="$(cygpath -w "$(mktemp -u)").zip"
TMP_B64="$(mktemp)"

MENU_SRC="${1:-}"
MENUID=""
if [ -n "$MENU_SRC" ]; then
  [ -f "$MENU_SRC" ] || { echo "ΛΑΘΟΣ: δεν βρέθηκε ο κατάλογος '$MENU_SRC'" >&2; exit 1; }
  grep -q '"Categories"' "$MENU_SRC" || { echo "ΛΑΘΟΣ: το '$MENU_SRC' δεν μοιάζει με menu.json" >&2; exit 1; }
  MENUID="v$VERSION"
fi

# Πελατολόγιο: ταξιδεύει ΠΑΝΤΑ μαζί με το setup (ζητήθηκε ρητά), αν υπάρχει στην Επιφάνεια.
#
# Μπαίνει απλώς ΔΙΠΛΑ ΣΤΟ EXE ως «pelates.json». Το setup ΔΕΝ αγγίζει τον πελατολόγιο του μαγαζιού —
# τη δουλειά την κάνει το ίδιο το πρόγραμμα στο πρώτο άνοιγμα (βλ. CustomerStore.ImportSeedIfPresent),
# που ΠΡΟΣΘΕΤΕΙ όσους λείπουν χωρίς να σβήνει κανέναν υπάρχοντα.
CUST_SRC="$DESKTOP/customers.json"

# Καθαρίζει παλιά setup.bat στην Επιφάνεια (και το παλιό όνομα χωρίς έκδοση) — ώστε να μένει πάντα
# μόνο ΕΝΑ, με το σωστό όνομα, χωρίς σύγχυση για το ποιο είναι το τρέχον.
rm -f "$DESKTOP"/PittaPOS2-Setup-v*.bat

echo "Publishing (self-contained, single-file)..."
dotnet publish src/PittaPos.App/PittaPos.App.csproj -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "$PUBLISH_DIR"

if [ -n "$MENU_SRC" ]; then
  echo "Bundling catalogue from $MENU_SRC ..."
  cp "$MENU_SRC" "$PUBLISH_DIR/katalogos.json"
else
  # Καθάρισμα από προηγούμενη έκδοση που ΕΙΧΕ κατάλογο: ο φάκελος publish δεν αδειάζει μόνος του,
  # οπότε ένα ξεχασμένο katalogos.json θα ταξίδευε μέσα στο setup χωρίς λόγο — και θα καθόταν στο
  # C:\PittaPOS2 του μαγαζιού σαν παλιός κατάλογος που δεν διαβάζει κανείς.
  rm -f "$PUBLISH_DIR/katalogos.json"
fi

# Το εικονίδιο της συντόμευσης «Ζωντανές Παραγγελίες» ΔΙΠΛΑ ΣΤΟ EXE. Αντιγράφεται εδώ ρητά και δεν
# αφήνεται στο csproj: με PublishSingleFile το Content δεν έφτανε στο publish/ (δοκιμάστηκε), οπότε η
# συντόμευση θα έπαιρνε σιωπηλά το εικονίδιο του ταμείου και τα δύο εικονίδια θα ήταν ίδια.
cp src/PittaPos.App/Assets/live-orders.ico "$PUBLISH_DIR/live-orders.ico"

if [ -f "$CUST_SRC" ]; then
  echo "Bundling customers from $CUST_SRC ..."
  cp "$CUST_SRC" "$PUBLISH_DIR/pelates.json"
else
  echo "ΠΡΟΣΟΧΗ: δεν βρέθηκε πελατολόγιο στο $CUST_SRC — το setup φεύγει ΧΩΡΙΣ πελάτες."
  rm -f "$PUBLISH_DIR/pelates.json"
fi

echo "Zipping publish output..."
powershell -NoProfile -Command "Compress-Archive -Path '$(cygpath -w "$PUBLISH_DIR")\*' -DestinationPath '$TMP_ZIP' -Force"

echo "Base64-encoding..."
powershell -NoProfile -Command "[Convert]::ToBase64String([IO.File]::ReadAllBytes('$TMP_ZIP')) | Set-Content -Encoding ascii -NoNewline '$(cygpath -w "$TMP_B64")'"

echo "Writing $BAT ..."
# UTF-8 BOM + CRLF wrapper, byte-identical header to the existing installer (πέρα από το version label).
printf '\xEF\xBB\xBF' > "$BAT"
sed -e "s/{{VERSION}}/$VERSION/" -e "s/{{MENUID}}/$MENUID/" <<'HEADER' | sed 's/$/\r/' >> "$BAT"
@echo off
chcp 65001 >nul
title Pitta POS 2 - Εγκατάσταση (v{{VERSION}})
rem  Άδειο = το setup ΔΕΝ αγγίζει τον κατάλογο του μαγαζιού (ο κανόνας).
set "MENUID={{MENUID}}"
echo.
echo   Pitta POS 2 - Εγκατάσταση (v{{VERSION}})
echo   ------------------------
echo.
echo Κλείσιμο του προγράμματος αν είναι ανοιχτό...
taskkill /IM "PittaPos2.App.exe" /F >nul 2>&1
rem  Οι ρυθμίσεις (IP δεύτερου ταμείου, διεύθυνση μαγαζιού, εκτυπωτής, PIN) και ΟΛΑ τα δεδομένα
rem  (πελάτες, ιστορικό, κατάλογος) ζουν στο %%AppData%%\PittaPos2 — η εγκατάσταση γράφει μόνο στο
rem  C:\PittaPOS2, οπότε δεν τα ακουμπάει καν. Το αντίγραφο παρακάτω υπάρχει για κάθε ενδεχόμενο.
echo Φύλαξη αντιγράφου των ρυθμίσεων...
powershell -NoProfile -ExecutionPolicy Bypass -Command "& { try { $s = Join-Path (Join-Path $env:AppData 'PittaPos2') 'settings.json'; if (Test-Path $s) { Copy-Item -LiteralPath $s -Destination ($s + '.bak-' + (Get-Date -Format 'yyyyMMdd-HHmm')) -Force } } catch { } }"
echo Προετοιμασία αρχείων, περίμενε λίγο...
powershell -NoProfile -ExecutionPolicy Bypass -Command "& { $ErrorActionPreference='Stop'; try { Get-Process -Name 'PittaPos2.App' -ErrorAction SilentlyContinue | Stop-Process -Force; $dest='C:\PittaPOS2'; $exe = Join-Path $dest 'PittaPos2.App.exe'; for ($i = 0; $i -lt 20; $i++) { if (-not (Test-Path $exe)) { break }; try { $fs = [System.IO.File]::Open($exe, 'Open', 'ReadWrite', 'None'); $fs.Close(); break } catch { Start-Sleep -Milliseconds 300 } }; $lines = Get-Content -LiteralPath '%~f0'; $startIdx = ($lines | Select-String -Pattern '^:PAYLOAD$' | Select-Object -First 1).LineNumber; $b64 = ($lines[$startIdx..($lines.Count-1)] -join ''); $bytes = [System.Convert]::FromBase64String($b64); $zip = Join-Path $env:TEMP 'pittapos2-install.zip'; [System.IO.File]::WriteAllBytes($zip, $bytes); if (-not (Test-Path $dest)) { New-Item -ItemType Directory -Path $dest | Out-Null }; Expand-Archive -Path $zip -DestinationPath $dest -Force; Remove-Item $zip -Force; $s = (New-Object -ComObject WScript.Shell).CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) 'Pitta POS 2.lnk')); $s.TargetPath = $exe; $s.WorkingDirectory = $dest; $s.IconLocation = $exe; $s.Save(); $nm = -join ([int[]](0x396,0x3C9,0x3BD,0x3C4,0x3B1,0x3BD,0x3AD,0x3C2,0x20,0x3A0,0x3B1,0x3C1,0x3B1,0x3B3,0x3B3,0x3B5,0x3BB,0x3AF,0x3B5,0x3C2) | ForEach-Object { [char]$_ }); $b = (New-Object -ComObject WScript.Shell).CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) ($nm + '.lnk'))); $b.TargetPath = $exe; $b.Arguments = '--live'; $b.WorkingDirectory = $dest; $ico = Join-Path $dest 'live-orders.ico'; $b.IconLocation = $(if (Test-Path $ico) { $ico } else { $exe }); $b.Description = $nm; $b.Save(); Write-Host 'OK' } catch { Write-Host ('SFALMA: ' + $_.Exception.Message); exit 1 } }"
if errorlevel 1 (
  echo.
  echo   ΣΦΑΛΜΑ — η εγκατάσταση ΔΕΝ ολοκληρώθηκε. Δες το μήνυμα παραπάνω ^(π.χ. αρχείο κλειδωμένο,
  echo   κλείσε το πρόγραμμα χειροκίνητα και ξανατρέξε αυτό το .bat^).
  echo.
  pause
  exit /b 1
)
if "%MENUID%"=="" goto :meta_katalogo
echo Τοποθέτηση του καταλόγου...
powershell -NoProfile -ExecutionPolicy Bypass -Command "& { $ErrorActionPreference='Stop'; try { $id='%MENUID%'; $src='C:\PittaPOS2\katalogos.json'; if (-not (Test-Path $src)) { exit 2 }; $dir=Join-Path $env:AppData 'PittaPos2'; New-Item -ItemType Directory -Force -Path $dir | Out-Null; $marker=Join-Path $dir 'menu-version.txt'; if ((Test-Path $marker) -and ((Get-Content -LiteralPath $marker -Raw).Trim() -eq $id)) { exit 2 }; $cur=Join-Path $dir 'menu.json'; if (Test-Path $cur) { Copy-Item -LiteralPath $cur -Destination ($cur + '.bak-' + (Get-Date -Format 'yyyyMMdd-HHmm')) -Force }; Copy-Item -LiteralPath $src -Destination $cur -Force; Set-Content -LiteralPath $marker -Value $id -Encoding utf8; exit 0 } catch { Write-Host ('SFALMA: ' + $_.Exception.Message); exit 1 } }"
if errorlevel 2 goto :katalogos_idi
if errorlevel 1 goto :katalogos_apotyxia
echo   - ο νέος κατάλογος μπήκε ^(ο παλιός φυλάχτηκε δίπλα του ως menu.json.bak-...^)
goto :meta_katalogo
:katalogos_idi
echo   - ο κατάλογος αυτής της έκδοσης είχε ήδη μπει, δεν άλλαξε τίποτα
goto :meta_katalogo
:katalogos_apotyxia
echo.
echo   ΠΡΟΣΟΧΗ: το πρόγραμμα εγκαταστάθηκε κανονικά, αλλά ο ΚΑΤΑΛΟΓΟΣ δεν μπήκε.
echo   Ο παλιός κατάλογος είναι ανέπαφος. Δες το μήνυμα παραπάνω.
echo.
:meta_katalogo
echo.
echo   Οι ρυθμίσεις που κρατήθηκαν:
powershell -NoProfile -ExecutionPolicy Bypass -Command "& { try { $s = Join-Path (Join-Path $env:AppData 'PittaPos2') 'settings.json'; if (-not (Test-Path $s)) { Write-Host '   - (καθαρή εγκατάσταση, δεν υπήρχαν ρυθμίσεις)'; exit 0 }; $j = Get-Content -LiteralPath $s -Raw | ConvertFrom-Json; $net = if ($j.NetworkMode -eq 'client') { 'ΔΕΥΤΕΡΟ ΤΑΜΕΙΟ -> ' + $j.HostAddress } else { 'ΚΥΡΙΟ ΤΑΜΕΙΟ' }; Write-Host ('   - Δίκτυο: ' + $net); Write-Host ('   - Εκτυπωτής: ' + $(if ($j.PrinterName) { $j.PrinterName } else { '(κανένας)' })); Write-Host ('   - Διεύθυνση μαγαζιού: ' + $(if ($j.ShopAddress) { $j.ShopAddress } else { '(δεν έχει οριστεί)' })) } catch { Write-Host '   - (δεν διαβάστηκαν)' } }"
echo.
echo   Ολοκληρώθηκε! Βρες το εικονίδιο "Pitta POS 2" στην Επιφάνεια Εργασίας.
echo   Δίπλα του μπήκε και το "Zontanes Paraggelies" ^(με τον ντελιβερά^) — οι Ζωντανές Παραγγελίες
echo   ανοίγουν πλέον σε δικό τους παράθυρο, ξεχωριστά από το ταμείο.
echo.
pause
exit /b
:PAYLOAD
HEADER
fold -w 100 "$TMP_B64" | sed 's/$/\r/' >> "$BAT"

rm -f "$TMP_ZIP" "$TMP_B64"
echo "Done: $BAT"
