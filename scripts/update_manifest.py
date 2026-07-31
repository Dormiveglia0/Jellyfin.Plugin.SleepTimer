#!/usr/bin/env python3
"""Insert or replace one Jellyfin plugin release in manifest.json."""

from __future__ import annotations

import argparse
import json
import re
from pathlib import Path


VERSION_PATTERN = re.compile(r"^\d+\.\d+\.\d+\.\d+$")
CHECKSUM_PATTERN = re.compile(r"^[0-9a-fA-F]{32}$")


def parse_arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--manifest", type=Path, required=True)
    parser.add_argument("--version", required=True)
    parser.add_argument("--target-abi", required=True)
    parser.add_argument("--source-url", required=True)
    parser.add_argument("--checksum", required=True)
    parser.add_argument("--timestamp", required=True)
    parser.add_argument("--changelog", required=True)
    return parser.parse_args()


def main() -> None:
    arguments = parse_arguments()
    if not VERSION_PATTERN.fullmatch(arguments.version):
        raise ValueError(f"Invalid plugin version: {arguments.version}")
    if not VERSION_PATTERN.fullmatch(arguments.target_abi):
        raise ValueError(f"Invalid target ABI: {arguments.target_abi}")
    if not CHECKSUM_PATTERN.fullmatch(arguments.checksum):
        raise ValueError("Jellyfin repository checksums must be 32-character MD5 values")
    if not arguments.source_url.startswith("https://"):
        raise ValueError("Release source URL must use HTTPS")

    manifest = json.loads(arguments.manifest.read_text(encoding="utf-8"))
    if not isinstance(manifest, list) or len(manifest) != 1:
        raise ValueError("Expected a manifest with exactly one plugin")

    plugin = manifest[0]
    versions = [
        release
        for release in plugin.get("versions", [])
        if not (
            release.get("version") == arguments.version
            and release.get("targetAbi") == arguments.target_abi
        )
    ]
    versions.insert(
        0,
        {
            "version": arguments.version,
            "changelog": arguments.changelog,
            "targetAbi": arguments.target_abi,
            "sourceUrl": arguments.source_url,
            "checksum": arguments.checksum.lower(),
            "timestamp": arguments.timestamp,
        },
    )
    plugin["versions"] = versions
    arguments.manifest.write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )


if __name__ == "__main__":
    main()
