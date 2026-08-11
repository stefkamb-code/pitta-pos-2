@echo off
chcp 65001 >nul
title Νέος κατάλογος - Pitta POS 2
setlocal

set "NEW=%~dp0menu-neo.json"
set "DIR=%AppData%\PittaPos2"
set "CUR=%DIR%\menu.json"

echo.
echo   ΝΕΟΣ ΚΑΤΑΛΟΓΟΣ ΓΙΑ ΤΟ "PITTA POS 2"
echo   ----------------------------------
echo.
echo   Βάζει τον νέο κατάλογο στη θέση του τωρινού.
echo   Κρατάει ΑΝΤΙΓΡΑΦΟ του τωρινού - δεν χάνεται τίποτα.
echo.
echo   ΔΕΝ πειράζει τίποτα άλλο: παραγγελίες, ιστορικό, πελάτες,
echo   ρυθμίσεις και αρχείο ημερών μένουν ακριβώς όπως είναι.
echo.

if not exist "%NEW%" (
  echo   ΛΑΘΟΣ: δεν βρέθηκε το "menu-neo.json" δίπλα σε αυτό το αρχείο.
  echo   Πρέπει τα δύο αρχεία να είναι στον ΙΔΙΟ φάκελο.
  echo.
  pause
  exit /b 1
)

if not exist "%CUR%" (
  echo   ΛΑΘΟΣ: δεν βρέθηκε κατάλογος στο:
  echo     %CUR%
  echo   Τρέξε το στον υπολογιστή που δουλεύει το "Pitta POS 2".
  echo.
  pause
  exit /b 1
)

set /p answer=  Να προχωρήσω; Πάτα N για ναι, οτιδήποτε άλλο για ακύρωση:
if /i not "%answer%"=="N" goto :cancelled
echo.

echo   Κλείσιμο του προγράμματος αν είναι ανοιχτό...
rem  ΑΠΑΡΑΙΤΗΤΟ: το ταμείο κρατά τον κατάλογο στη ΜΝΗΜΗ και τον ξαναγράφει στο αρχείο με την
rem  πρώτη αποθήκευση - με ανοιχτό πρόγραμμα, ο νέος κατάλογος θα χανόταν στο πρώτο άγγιγμα.
taskkill /IM "PittaPos2.App.exe" /F >nul 2>&1
timeout /t 3 /nobreak >nul

for /f %%i in ('powershell -NoProfile -Command "Get-Date -Format yyyyMMdd-HHmm"') do set "STAMP=%%i"
echo   Αντίγραφο του τωρινού καταλόγου...
copy /y "%CUR%" "%DIR%\menu.json.bak-%STAMP%" >nul
if errorlevel 1 (
  echo.
  echo   Το αντίγραφο ΑΠΕΤΥΧΕ - σταματάω εδώ, δεν άλλαξε τίποτα.
  echo.
  pause
  exit /b 1
)
echo     - φυλάχτηκε ως menu.json.bak-%STAMP%

echo   Τοποθέτηση του νέου καταλόγου...
copy /y "%NEW%" "%CUR%" >nul
if errorlevel 1 (
  echo.
  echo   Η αντιγραφή απέτυχε. Ο παλιός κατάλογος είναι στη θέση του.
  echo.
  pause
  exit /b 1
)

echo.
echo   Ολοκληρώθηκε. Άνοιξε το "Pitta POS 2" και τσέκαρε τον κατάλογο.
echo.
echo   Αν κάτι δεν σου αρέσει, ο παλιός είναι εδώ:
echo     %DIR%\menu.json.bak-%STAMP%
echo   (σβήσε το menu.json και μετονόμασε το αντίγραφο σε menu.json)
echo.
pause
exit /b

:cancelled
echo.
echo   Ακυρώθηκε - δεν άλλαξε τίποτα.
echo.
pause
exit /b
