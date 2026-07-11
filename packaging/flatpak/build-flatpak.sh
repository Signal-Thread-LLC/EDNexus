#!/usr/bin/env bash
#
# Produces the self-contained linux-x64 payload that the EDNexus Flatpak packages.
#
# Flathub builds in a network-restricted sandbox, so we do NOT run `dotnet restore` there:
# instead we publish a self-contained bundle here, tar it, and the manifest consumes that
# tarball as a checksummed `archive` source.
#
# Usage:   ./build-flatpak.sh [version]        e.g. ./build-flatpak.sh 0.1.0
# SENTRY_DSN, if exported, is injected into the published app (kept out of the repo).
set -euo pipefail

VERSION="${1:-0.1.0}"
RUNTIME="linux-x64"

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
PUBLISH="$HERE/publish"
OUT="$HERE/out"
TARBALL="$OUT/ednexus-${VERSION}-${RUNTIME}.tar.gz"

rm -rf "$PUBLISH" "$OUT"
mkdir -p "$PUBLISH" "$OUT"

echo "==> Publishing self-contained $RUNTIME build..."
# InvariantGlobalization drops the ICU dependency, keeping the bundle self-contained.
dotnet publish "$ROOT/src/EDNexus.App/EDNexus.App.csproj" \
  --configuration Release \
  --runtime "$RUNTIME" --self-contained true \
  -p:Version="$VERSION" \
  -p:SentryDsn="${SENTRY_DSN:-}" \
  -p:InvariantGlobalization=true \
  --output "$PUBLISH"

echo "==> Creating tarball (files at archive root)..."
tar -czf "$TARBALL" -C "$PUBLISH" .

SHA="$(sha256sum "$TARBALL" | cut -d' ' -f1)"
cat <<EOF

==> Payload ready.
    tarball : $TARBALL
    sha256  : $SHA

Next steps for a Flathub release:
  1. Upload the tarball as a GitHub Release asset on tag v$VERSION.
  2. In io.github.Signal_Thread_LLC.EDNexus.yml set:
       url:    https://github.com/Signal-Thread-LLC/EDNexus/releases/download/v$VERSION/$(basename "$TARBALL")
       sha256: $SHA

Local test build (no upload needed) — temporarily swap the manifest's archive source for
a dir source pointing at ./publish (see README), then:
  flatpak-builder --user --install --force-clean build-dir \\
    "$HERE/io.github.Signal_Thread_LLC.EDNexus.yml"
  flatpak run io.github.Signal_Thread_LLC.EDNexus
EOF
