import importlib.util
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile

spec = importlib.util.spec_from_file_location('backup_saves', Path(__file__).resolve().parents[1] / 'tools/backup_saves.py')
backup_saves = importlib.util.module_from_spec(spec)
spec.loader.exec_module(backup_saves)

class BackupTests(unittest.TestCase):
    def test_closed_game_backup_restore_and_retention(self):
        with tempfile.TemporaryDirectory(prefix='nearbycraft-backup-test-') as directory:
            root = Path(directory)
            for relative in ['data/Saves/Test/World', 'data/GeneratedWorlds/Test', 'Mods/NearbyCraft']:
                (root / relative).mkdir(parents=True)
            save = root / 'data/Saves/Test/World/main.ttw'
            save.write_bytes(b'known test save')
            (root / 'data/GeneratedWorlds/Test/map_info.xml').write_bytes(b'world info')
            (root / 'Mods/NearbyCraft/config.json').write_bytes(b'{}')
            config = dict(data_root=str(root / 'data'), mods_root=str(root / 'Mods'), destination=str(root / 'backups'), keep=2)
            with patch.object(backup_saves, 'game_running', return_value=True):
                self.assertIsNone(backup_saves.backup(config))
            with patch.object(backup_saves, 'game_running', return_value=False):
                first = backup_saves.backup(config)
                self.assertEqual(len(backup_saves.verify(first)['sha256']), 3)
                self.assertIsNone(backup_saves.backup(config))
                # Test restoration into a disposable directory, never live saves.
                with zipfile.ZipFile(first) as archive:
                    archive.extractall(root / 'restore')
                self.assertEqual((root / 'restore/data/Saves/Test/World/main.ttw').read_bytes(), save.read_bytes())
                for number in range(2):
                    save.write_bytes(f'changed save {number}'.encode())
                    self.assertIsNotNone(backup_saves.backup(config))
                self.assertEqual(len(backup_saves.archives(root / 'backups')), 2)
                self.assertFalse(first.exists())
                self.assertFalse(list((root / 'backups').glob('*.partial')))
            save.write_bytes(b'changed during game launch')
            with patch.object(backup_saves, 'game_running', side_effect=[False, True]):
                with self.assertRaises(RuntimeError):
                    backup_saves.backup(config)
            self.assertEqual(len(backup_saves.archives(root / 'backups')), 2)
            self.assertFalse(list((root / 'backups').glob('*.partial')))

    def test_destination_inside_source_rejected(self):
        with tempfile.TemporaryDirectory(prefix='nearbycraft-backup-test-') as directory:
            root = Path(directory)
            with self.assertRaises(ValueError):
                backup_saves.backup(dict(data_root=str(root), mods_root=str(root), destination=str(root / 'backups')))

if __name__ == '__main__':
    unittest.main()
