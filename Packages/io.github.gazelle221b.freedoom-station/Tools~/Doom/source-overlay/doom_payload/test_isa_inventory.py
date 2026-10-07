"""Guard C/GPU suite parity and missing-test failure behavior."""
import contextlib
import io
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

import prepare_isa_tests as prep


class TestInventory(unittest.TestCase):
    def test_current_c_suite_contains_privileged_tests(self):
        names, digest = prep.test_inventory()
        self.assertEqual(len(names), 61)
        self.assertEqual({n for n in names if n.startswith(('rv32mi-', 'rv32si-'))},
                         {'rv32mi-p-mcsr', 'rv32mi-p-csr', 'rv32si-p-csr', 'rv32si-p-scall'})
        self.assertEqual(len(digest), 64)

    def inventory(self, body):
        with tempfile.TemporaryDirectory() as tmp:
            source = Path(tmp) / 'test.sh'
            source.write_bytes(body.encode())
            return prep.test_inventory(source)

    def test_crlf_comments_and_outside_exclusions(self):
        names, _ = self.inventory('TESTS="rv32ui-p-add\r\n# ignored\r\nrv32si-p-scall"\r\n'
                                 '# excluded rv32mi-p-scall\r\n')
        self.assertEqual(names, ['rv32ui-p-add', 'rv32si-p-scall'])

    def test_duplicate_is_rejected(self):
        with self.assertRaises(ValueError):
            self.inventory('TESTS="rv32ui-p-add\nrv32ui-p-add"')

    def test_empty_or_missing_inventory_is_rejected(self):
        for body in ('TESTS=""', '# no inventory'):
            with self.subTest(body=body), self.assertRaises(ValueError):
                self.inventory(body)

    def test_non_basename_is_rejected(self):
        with self.assertRaises(ValueError):
            self.inventory('TESTS="../rv32ui-p-add"')

    def test_missing_corpus_fails_and_records_incomplete_manifest(self):
        with tempfile.TemporaryDirectory() as tmp:
            args = ['prepare_isa_tests.py', '--data-dir', tmp, '--out-dir', tmp]
            with patch.object(sys, 'argv', args), contextlib.redirect_stdout(io.StringIO()):
                with self.assertRaises(SystemExit) as result:
                    prep.main()
            self.assertEqual(result.exception.code, 1)
            manifest = json.loads((Path(tmp) / 'manifest.json').read_text())
            self.assertFalse(manifest['complete'])
            self.assertEqual(len(manifest['expected']), 61)
            self.assertEqual(manifest['prepared'], [])


if __name__ == '__main__':
    unittest.main()
