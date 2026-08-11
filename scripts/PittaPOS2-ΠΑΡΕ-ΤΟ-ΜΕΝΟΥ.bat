@echo off
chcp 65001 >nul
title Αντίγραφο καταλόγου - Pitta POS 2
echo.
echo   ΑΝΤΙΓΡΑΦΟ ΤΟΥ ΚΑΤΑΛΟΓΟΥ ΤΟΥ ΤΑΜΕΙΟΥ
echo   ------------------------------------
echo.
echo   Βγάζει ένα αντίγραφο του καταλόγου στην Επιφάνεια Εργασίας.
echo   ΔΕΝ αλλάζει τίποτα - μόνο διαβάζει.
echo   Δεν χρειάζεται να κλείσεις το πρόγραμμα.
echo.

set "SRC=%AppData%\PittaPos2\menu.json"
set "DST=%USERPROFILE%\Desktop\menu-ΑΠΟ-ΤΟ-ΤΑΜΕΙΟ.json"

if not exist "%SRC%" (
  echo   ΔΕΝ ΒΡΕΘΗΚΕ ο κατάλογος στο:
  echo     %SRC%
  echo.
  echo   Τρέξε αυτό το αρχείο στον υπολογιστή που δουλεύει το "Pitta POS 2".
  echo.
  pause
  exit /b 1
)

copy /y "%SRC%" "%DST%" >nul
if errorlevel 1 (
  echo   Η αντιγραφή απέτυχε.
  echo.
  pause
  exit /b 1
)

echo   Έτοιμο. Μπήκε στην Επιφάνεια Εργασίας:
echo     menu-ΑΠΟ-ΤΟ-ΤΑΜΕΙΟ.json
echo.
for %%F in ("%SRC%") do echo   (τελευταία αλλαγή στο ταμείο: %%~tF)
echo.
echo   Στείλε μου αυτό το αρχείο.
echo.
pause
exit /b
