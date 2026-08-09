#!/usr/bin/env bash
# Ανεβάζει το patch του version.txt (π.χ. 1.0.3 -> 1.0.4). Καλείται ΜΟΝΟ όταν κλείνουμε δουλειά —
# πρώτο βήμα του release flow (bump -> commit -> make-setup), όχι σε κάθε dev build.
set -e
cd "$(dirname "$0")/.."

FILE="src/PittaPos.App/version.txt"
CUR="$(tr -d '[:space:]' < "$FILE")"
IFS='.' read -r MAJOR MINOR PATCH <<< "$CUR"
NEW="$MAJOR.$MINOR.$((PATCH + 1))"

echo "$NEW" > "$FILE"
echo "Version bumped: $CUR -> $NEW"
