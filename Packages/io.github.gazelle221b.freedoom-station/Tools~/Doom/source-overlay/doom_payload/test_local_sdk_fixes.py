import hashlib
import importlib.util
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch


spec = importlib.util.spec_from_file_location('local_sdk_fixes', Path(__file__).parent / 'unity/apply_local_sdk_fixes.py')
fixes = importlib.util.module_from_spec(spec)
spec.loader.exec_module(fixes)


class LocalSdkFixTests(unittest.TestCase):
    def fixture(self, root, newline=b'\n'):
        text = b'using VRC.Udon.Serialization.OdinSerializer;\n'
        for name in ('OnBeforeSerialize', 'OnAfterDeserialize'):
            text += ('void ISerializationCallbackReceiver.' + name + '()\n        {\n            OriginalCallback();\n        }\n').encode()
        text = text.replace(b'\n', newline)
        target = root / fixes.SDK
        target.parent.mkdir(parents=True)
        target.write_bytes(text)
        return target, text

    def test_preserves_original_backup_and_is_idempotent(self):
        for newline in (b'\n', b'\r\n'):
            with self.subTest(newline=newline), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                target, original = self.fixture(root)
                if newline == b'\r\n':
                    original = original.replace(b'\n', b'\r\n')
                    target.write_bytes(original)
                backup = root / 'backup'
                with patch.object(fixes, 'SDK_SHA', hashlib.sha256(original).hexdigest()):
                    first = fixes.apply(root, backup)
                    modified = target.read_bytes()
                    second = fixes.apply(root, backup)
                self.assertTrue(first[0]['changed'])
                self.assertFalse(second[0]['changed'])
                self.assertEqual((backup / fixes.SDK).read_bytes(), original)
                self.assertEqual(target.read_bytes(), modified)
                self.assertEqual(modified.count(b'if (this.SafeIsUnityNull()) return;'), 2)
                self.assertEqual(modified.count(b'\r\n'), 0 if newline == b'\n' else modified.count(b'\n'))

    def test_rejects_unknown_sdk_without_writing_or_backup(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            target, original = self.fixture(root)
            with self.assertRaisesRegex(ValueError, 'differs from pinned'):
                fixes.apply(root, root / 'backup')
            self.assertEqual(target.read_bytes(), original)
            self.assertFalse((root / 'backup').exists())

    def test_rejects_other_edits_to_previously_patched_sdk(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            target, original = self.fixture(root)
            with patch.object(fixes, 'SDK_SHA', hashlib.sha256(original).hexdigest()):
                fixes.apply(root, root / 'backup')
                target.write_bytes(target.read_bytes() + b'// unrelated edit\n')
                with self.assertRaisesRegex(ValueError, 'differs from the pinned original'):
                    fixes.apply(root, root / 'second-backup')
            self.assertFalse((root / 'second-backup').exists())


if __name__ == '__main__':
    unittest.main()
