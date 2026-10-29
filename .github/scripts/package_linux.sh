#!/usr/bin/env bash
#
# Assemble the Linux package from a published Canopy Studio.
#
#   .github/scripts/package_linux.sh <studio-dir> <version> [output-dir]
#
# Writes CanopyStudio-<version>-linux-x64.tar.gz, Canopy Studio self-contained with
# libcanopy_core.so beside it, under one top folder. Nothing ships until Canopy's
# check_linux_abi.py, from the submodule, agrees libcanopy_core.so starts on the distributions
# the release targets.

set -euo pipefail

if [ $# -lt 2 ] || [ $# -gt 3 ]; then
    echo "usage: $0 <studio-dir> <version> [output-dir]" >&2
    exit 2
fi

studio_dir=$1
version=$2
out_dir=${3:-dist}
repo_root=$(cd "$(dirname "$0")/../.." && pwd)

# Canopy Studio cannot start without these; the rest of the folder is the .NET runtime and
# Avalonia's native libraries, which the publish step owns.
for item in CanopyStudio CanopyStudio.dll libcanopy_core.so; do
    if [ ! -f "$studio_dir/$item" ]; then
        echo "error: the published Canopy Studio is missing $item" >&2
        exit 1
    fi
done

python3 "$repo_root/external/Canopy/.github/scripts/check_linux_abi.py" "$studio_dir/libcanopy_core.so"

mkdir -p "$out_dir"
stage_root="$out_dir/stage-linux"
rm -rf "$stage_root"

studio="$stage_root/CanopyStudio"
mkdir -p "$studio"
cp -r "$studio_dir/." "$studio/"
install -m 644 "$repo_root/LICENSE" "$repo_root/THIRD_PARTY_NOTICES.txt" "$studio/"
chmod +x "$studio/CanopyStudio"

# Fixed ownership, order and times so the same tree always produces the same tarball.
studio_tar="$out_dir/CanopyStudio-$version-linux-x64.tar.gz"
tar --sort=name --owner=0 --group=0 --numeric-owner \
    --mtime="@${SOURCE_DATE_EPOCH:-0}" \
    -czf "$studio_tar" -C "$stage_root" CanopyStudio
rm -rf "$stage_root"

echo
sha256sum "$studio_tar"
