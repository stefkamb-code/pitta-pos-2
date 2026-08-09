@echo off
chcp 65001 >nul
title Αφαίρεση Pitta POS - πρώτη εφαρμογή
echo.
echo   ΑΦΑΙΡΕΣΗ ΤΗΣ ΠΡΩΤΗΣ ΕΦΑΡΜΟΓΗΣ "Pitta POS"
echo   ------------------------------------------
echo.
echo   Θα αφαιρεθεί ΜΟΝΟ το πρόγραμμα:
echo     - ο φάκελος C:\PittaPOS
echo     - η συντόμευση "Pitta POS" από την Επιφάνεια Εργασίας
echo.
echo   ΔΕΝ διαγράφονται τα δεδομένα σου. Παραγγελίες, ιστορικό, αρχείο ημερών,
echo   πελάτες, κατάλογος και ρυθμίσεις μένουν στον φάκελο:
echo     %%AppData%%\PittaPos
echo.
echo   Το "Pitta POS 2" ΔΕΝ επηρεάζεται καθόλου.
echo.
set /p answer=  Να προχωρήσω; Πάτα N για ναι, οτιδήποτε άλλο για ακύρωση:
if /i not "%answer%"=="N" goto :cancelled
echo.
echo   Κλείσιμο του προγράμματος αν είναι ανοιχτό...
taskkill /IM "PittaPos.App.exe" /F >nul 2>&1
timeout /t 2 /nobreak >nul

echo   Αφαίρεση του φακέλου C:\PittaPOS ...
if exist "C:\PittaPOS" (
  rd /s /q "C:\PittaPOS"
  if exist "C:\PittaPOS" (
    echo.
    echo   ΠΡΟΣΟΧΗ: ο φάκελος δεν αφαιρέθηκε. Μάλλον κάποιο αρχείο είναι ανοιχτό.
    echo   Κλείσε το πρόγραμμα και ξανατρέξε αυτό το αρχείο.
    echo.
    pause
    exit /b 1
  )
) else (
  echo   - δεν βρέθηκε, μάλλον έχει ήδη αφαιρεθεί
)

echo   Αφαίρεση της συντόμευσης...
if exist "%USERPROFILE%\Desktop\Pitta POS.lnk" del /f /q "%USERPROFILE%\Desktop\Pitta POS.lnk"
if exist "%PUBLIC%\Desktop\Pitta POS.lnk" del /f /q "%PUBLIC%\Desktop\Pitta POS.lnk"

echo.
echo   Ολοκληρώθηκε. Η πρώτη εφαρμογή αφαιρέθηκε.
echo.
echo   Τα δεδομένα της παραμένουν στο %%AppData%%\PittaPos - αν κάποια στιγμή
echo   τα θελήσεις, είναι όλα εκεί. Αν θες να φύγουν κι αυτά, σβήσε τον φάκελο
echo   χειροκίνητα.
echo.
pause
exit /b
