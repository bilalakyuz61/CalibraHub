#!/usr/bin/env bash
# CalibraHub — otomatik commit (Stop hook).
# Claude her yanıtını bitirdiğinde src/ altındaki değişiklikleri jenerik
# mesajla commit'ler. Yalnızca src/ değişikliği varsa çalışır; yoksa no-op.
# Not: sadece src/ stage'lenir → run_out.txt / *.log / CalibraHub.sln / .claude
# dışarıda kalır (elle /commit ile aynı kapsam).

REPO="/d/JetBrainsRider/Projeler/CalibraHub"
cd "$REPO" 2>/dev/null || exit 0

# src/ altında değişiklik yoksa çık
if [ -z "$(git status --porcelain -- src/ 2>/dev/null)" ]; then
  exit 0
fi

git add -- src/ 2>/dev/null

# Gerçekten stage'lenen bir şey yoksa çık (ör. yalnızca ignore'lı dosyalar)
if git diff --cached --quiet -- src/ 2>/dev/null; then
  exit 0
fi

n=$(git diff --cached --name-only -- src/ 2>/dev/null | wc -l | tr -d ' ')
stamp=$(date '+%Y-%m-%d %H:%M')
git commit -q \
  -m "chore(auto): $stamp - $n dosya" \
  -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>" \
  >/dev/null 2>&1

exit 0
