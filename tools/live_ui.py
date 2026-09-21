#!/usr/bin/env python3
"""Validate and sync NearbyCraft XUi XML for the game's live XUi reload command."""

import argparse
import os
import shutil
import subprocess
import sys
from pathlib import Path
from xml.etree import ElementTree


REPO = Path(__file__).resolve().parents[1]
DEFAULT_GAME = Path.home() / ".local/share/Steam/steamapps/common/7 Days To Die"
RELATIVE_WINDOW = Path("Config/XUi_InGame/windows.xml")
BACKUP = REPO / "dist/live-ui-backup" / RELATIVE_WINDOW
ACTIVE = REPO / "dist/live-ui-backup/.active"


def paths(game: Path) -> tuple[Path, Path]:
    source = REPO / "package" / RELATIVE_WINDOW
    installed = game / "Mods/NearbyCraft" / RELATIVE_WINDOW
    if not source.is_file():
        raise SystemExit(f"Missing source UI: {source}")
    if not installed.is_file():
        raise SystemExit(f"NearbyCraft is not installed at: {installed}")
    return source, installed


def verify(game: Path, source: Path) -> None:
    ElementTree.parse(source)
    result = subprocess.run(
        [sys.executable, str(REPO / "tools/verify_package.py"), str(game)],
        cwd=REPO,
        check=False,
    )
    if result.returncode:
        raise SystemExit("UI sync stopped because package verification failed.")


def atomic_copy(source: Path, destination: Path) -> None:
    temporary = destination.with_name(destination.name + ".nearbycraft-live")
    shutil.copy2(source, temporary)
    os.replace(temporary, destination)


def install(game: Path) -> None:
    source, installed = paths(game)
    verify(game, source)
    if not ACTIVE.exists():
        BACKUP.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(installed, BACKUP)
        ACTIVE.write_text(str(installed) + "\n", encoding="utf-8")
    atomic_copy(source, installed)
    print(f"SYNCED: {installed}")
    print("In the running game's F1 console, enter: xui reload nearbycraft_workshop")
    print("Reopen Production if it was closed. A clean game launch is still required after DLL changes.")


def restore(game: Path) -> None:
    _, installed = paths(game)
    if not BACKUP.is_file():
        raise SystemExit(f"No live-UI backup exists at: {BACKUP}")
    ElementTree.parse(BACKUP)
    atomic_copy(BACKUP, installed)
    if ACTIVE.exists():
        ACTIVE.unlink()
    print(f"RESTORED: {installed}")
    print("In the running game's F1 console, enter: xui reload nearbycraft_workshop")


parser = argparse.ArgumentParser(
    description="Sync validated production-window XML into a running 7 Days to Die installation."
)
parser.add_argument("action", nargs="?", choices=("install", "restore", "check"), default="install")
parser.add_argument("--game", type=Path, default=DEFAULT_GAME, help="7 Days to Die installation root")
args = parser.parse_args()
game_root = args.game.expanduser().resolve()

if args.action == "restore":
    restore(game_root)
elif args.action == "install":
    install(game_root)
else:
    source_path, _ = paths(game_root)
    verify(game_root, source_path)
    print("PASS: production UI XML is valid and the full package checks pass.")
