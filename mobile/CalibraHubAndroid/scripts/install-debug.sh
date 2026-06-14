#!/usr/bin/env bash
# Tek tıkla debug APK üret + emulator'a kur + uygulamayı başlat.
# Önkoşul: Android emulator (CalibraHub-Pixel6-API34) çalışıyor olmalı.
set -e

cd "$(dirname "$0")/.."

echo "→ Building debug APK..."
./gradlew :app:installDebug

echo "→ Launching MainActivity..."
adb shell am start -n com.calibrahub.app/.MainActivity

echo "✓ Done. App started on connected emulator/device."
