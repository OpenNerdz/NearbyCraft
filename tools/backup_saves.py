#!/usr/bin/env python3
"""Linux/Proton closed-game backups. Never restores or edits game files."""
import argparse
import datetime
import fcntl
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import zipfile

ARCHIVE_NAME = re.compile(r'nearbycraft-\d{8}T\d{12}Z\.zip$')
IDENTITY = 'NearbyCraft closed-game backup v1'

def game_running():
    names = {'7daystodie', '7daystodie.exe', '7daystodie.x86_64', '7daystodie_server.x86_64', '7daystodieserver.exe'}
    for process in Path('/proc').iterdir():
        if not process.name.isdigit():
            continue
        try:
            arguments = (process / 'cmdline').read_bytes().decode(errors='replace').split('\0')
            if any(argument.replace('\\', '/').rsplit('/', 1)[-1].lower() in names for argument in arguments):
                return True
        except (FileNotFoundError, PermissionError, ProcessLookupError):
            pass
    return False

def collect(data_root, mods_root):
    files = {}
    for label, root in [('data/Saves', data_root / 'Saves'), ('data/GeneratedWorlds', data_root / 'GeneratedWorlds'), ('Mods', mods_root)]:
        if not root.is_dir():
            raise RuntimeError(f'Required source directory missing: {root}')
        for path in sorted(root.rglob('*')):
            if path.is_symlink():
                raise RuntimeError(f'Refusing symlink in backup source: {path}')
            if path.is_file():
                stat = path.stat()
                files[f'{label}/{path.relative_to(root).as_posix()}'] = (path, stat.st_size, stat.st_mtime_ns)
    return files

def fingerprint(files):
    return hashlib.sha256(json.dumps([(name, size, mtime) for name, (_, size, mtime) in files.items()]).encode()).hexdigest()

def archives(destination):
    return sorted(path for path in destination.iterdir() if path.is_file() and not path.is_symlink() and ARCHIVE_NAME.fullmatch(path.name))

def verify(path):
    with zipfile.ZipFile(path) as archive:
        manifest = json.loads(archive.read('manifest.json'))
        if manifest.get('format') != IDENTITY:
            raise RuntimeError('Unrecognized backup format')
        expected = set(manifest['sha256']) | {'manifest.json'}
        if len(archive.namelist()) != len(expected) or set(archive.namelist()) != expected:
            raise RuntimeError('Archive member mismatch')
        for name, expected_hash in manifest['sha256'].items():
            relative = PurePosixPath(name)
            if relative.is_absolute() or '..' in relative.parts or '\\' in name:
                raise RuntimeError('Unsafe archive path')
            with archive.open(name) as member:
                actual = hashlib.file_digest(member, 'sha256').hexdigest()
            if actual != expected_hash:
                raise RuntimeError(f'Checksum mismatch: {name}')
    return manifest

def backup(config):
    data_root = Path(config['data_root']).resolve(strict=True)
    mods_root = Path(config['mods_root']).resolve(strict=True)
    destination = Path(config['destination']).resolve()
    keep = int(config.get('keep', 8))
    if not 2 <= keep <= 32:
        raise ValueError('Retention must be between 2 and 32 backups')
    if destination == data_root or destination.is_relative_to(data_root) or destination == mods_root or destination.is_relative_to(mods_root):
        raise ValueError('Backup destination must be outside game data and Mods')
    destination.mkdir(parents=True, exist_ok=True, mode=0o700)
    with (destination / '.backup.lock').open('a') as lock:
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            print('SKIP: Another backup is active.')
            return None
        if game_running():
            print('SKIP: Game is running; no live-save copy attempted.')
            return None
        files = collect(data_root, mods_root)
        signature = fingerprint(files)
        existing = archives(destination)
        if existing:
            try:
                with zipfile.ZipFile(existing[-1]) as previous:
                    previous_manifest = json.loads(previous.read('manifest.json'))
                if previous_manifest.get('format') == IDENTITY and previous_manifest.get('fingerprint') == signature:
                    print('SKIP: Saves, worlds and mods have not changed.')
                    return None
            except (OSError, ValueError, KeyError, zipfile.BadZipFile):
                pass  # A damaged previous backup must not prevent a new one.
        needed = sum(size for _, size, _ in files.values()) + 256 * 1024 * 1024
        if shutil.disk_usage(destination).free < needed:
            raise RuntimeError('Insufficient free space for a conservative full-backup estimate')
        timestamp = datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%S%fZ')
        final = destination / f'nearbycraft-{timestamp}.zip'
        temporary = final.with_suffix('.partial')
        manifest = {'format': IDENTITY, 'created_utc': timestamp, 'fingerprint': signature,
                    'data_root': str(data_root), 'mods_root': str(mods_root), 'sha256': {}}
        try:
            with zipfile.ZipFile(temporary, 'x', zipfile.ZIP_DEFLATED, compresslevel=3) as archive:
                for name, (path, _, _) in files.items():
                    digest = hashlib.sha256()
                    with path.open('rb') as source, archive.open(name, 'w', force_zip64=True) as member:
                        while chunk := source.read(1024 * 1024):
                            member.write(chunk)
                            digest.update(chunk)
                    manifest['sha256'][name] = digest.hexdigest()
                archive.writestr('manifest.json', json.dumps(manifest, indent=2))
            if game_running() or fingerprint(collect(data_root, mods_root)) != signature:
                raise RuntimeError('Game started or files changed during backup; discarded incomplete snapshot')
            verify(temporary)
            with temporary.open('rb') as handle:
                os.fsync(handle.fileno())
            temporary.rename(final)
            directory_fd = os.open(destination, os.O_RDONLY)
            try:
                os.fsync(directory_fd)
            finally:
                os.close(directory_fd)
        finally:
            if temporary.exists():
                temporary.unlink()
        # Prune only recognized, verified backups in this dedicated directory.
        # A successful new backup always exists before retention removes anything.
        for obsolete in archives(destination)[:-keep]:
            verify(obsolete)
            obsolete.unlink()
        print(f'BACKUP: {final} ({len(files)} files, checksums verified; retain {keep})')
        return final

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument('--config', type=Path)
    group.add_argument('--verify', type=Path)
    args = parser.parse_args()
    if args.verify:
        result = verify(args.verify)
        print(f'PASS: {len(result["sha256"])} archived files verified.')
    else:
        backup(json.loads(args.config.read_text()))
