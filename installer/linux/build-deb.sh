#!/usr/bin/env bash
#
# Builds the EDNexus Linux package (.deb) with FPM (https://github.com/jordansissel/fpm).
#
# Publishes a self-contained linux-x64 build and packages it so it installs to:
#     /opt/signal-and-thread/ednexus/          (the Linux equivalent of
#                                                "Program Files\Signal & Thread\EDNexus")
# plus a launcher at /usr/local/bin/ednexus and a desktop entry.
# User preferences live in ~/EDNexus (Documents), never in the install dir.
#
# Prereqs: .NET 10 SDK, Ruby, and fpm (`gem install fpm`).
# Usage:   ./build-deb.sh [version] [runtime]     e.g. ./build-deb.sh 0.1.0 linux-x64
# SENTRY_DSN, if exported, is injected into the published app (kept out of the repo).
set -euo pipefail

VERSION="${1:-0.1.0}"
RUNTIME="${2:-linux-x64}"

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
STAGE="$HERE/stage"
OUT="$HERE/out"
INSTALL_DIR="/opt/signal-and-thread/ednexus"

rm -rf "$STAGE" "$OUT"
mkdir -p "$OUT" "$STAGE$INSTALL_DIR" "$STAGE/usr/local/bin" \
  "$STAGE/usr/share/applications" "$STAGE/usr/share/icons/hicolor/256x256/apps"

echo "==> Publishing EDNexus.App ($RUNTIME, self-contained)..."
# InvariantGlobalization avoids a hard ICU dependency, keeping the package portable across distros.
dotnet publish "$ROOT/src/EDNexus.App/EDNexus.App.csproj" \
  --configuration Release \
  --runtime "$RUNTIME" --self-contained true \
  -p:Version="$VERSION" \
  -p:SentryDsn="${SENTRY_DSN:-}" \
  -p:InvariantGlobalization=true \
  --output "$STAGE$INSTALL_DIR"

chmod +x "$STAGE$INSTALL_DIR/EDNexus.App" || true

# Launcher on PATH.
cat > "$STAGE/usr/local/bin/ednexus" <<EOF
#!/bin/sh
exec "$INSTALL_DIR/EDNexus.App" "\$@"
EOF
chmod +x "$STAGE/usr/local/bin/ednexus"

# Desktop entry + app icon.
cp "$HERE/ednexus.desktop" "$STAGE/usr/share/applications/ednexus.desktop"
cp "$ROOT/assets/icons/ednexus-256.png" "$STAGE/usr/share/icons/hicolor/256x256/apps/ednexus.png"

echo "==> Packaging .deb (prefer debian/ if present)..."
if [ -f "$ROOT/debian/control" ]; then
  echo "==> Found debian/control; building with dpkg-buildpackage..."
  sudo apt-get update
  sudo apt-get install -y build-essential devscripts dpkg-dev debhelper-compat fakeroot
  (cd "$ROOT" && dpkg-buildpackage -us -uc -b)
  # Move produced .deb(s) to out
  for d in "$ROOT"/../*.deb; do
    if [ -f "$d" ]; then
      mv "$d" "$OUT/" || true
    fi
  done
  echo "==> Done: $(ls -1 "$OUT"/*.deb 2>/dev/null || true)"
  exit 0
fi

echo "==> Packaging .deb with fpm..."
fpm -s dir -t deb \
  -n ednexus -v "$VERSION" -a amd64 \
  --description "EDNexus - a does-it-all Elite Dangerous commander console." \
  --vendor "Signal & Thread LLC" \
  --maintainer "Signal & Thread LLC" \
  --url "https://github.com/Signal-Thread-LLC/EDNexus" \
  --license "Proprietary" \
  --depends libx11-6 --depends libfontconfig1 --depends libssl3 \
  -C "$STAGE" \
  -p "$OUT/ednexus_${VERSION}_amd64.deb" \
  .

echo "==> Done: $OUT/ednexus_${VERSION}_amd64.deb"
