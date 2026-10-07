"""Protect the shared virtual inventory and the test-environment-only adapter."""
import contextlib
import hashlib
import io
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

import isa_vm
import prepare_isa_tests as prep


class VirtualTests(unittest.TestCase):
    def test_inventory_has_every_integer_virtual_case(self):
        names, digest = isa_vm.inventory()
        physical, pdigest = prep.test_inventory()
        self.assertEqual(digest, pdigest)
        self.assertEqual(len(names), 57)
        self.assertEqual([sum(n.startswith(g) for n in names)
                          for g in ('rv32ui-', 'rv32um-', 'rv32ua-')], [39, 8, 10])
        self.assertEqual(names, [n.replace('-p-', '-v-') for n in physical
                                if not n.startswith(('rv32mi-', 'rv32si-'))])

    def test_adapter_preserves_handler_and_failure_encoding(self):
        before = ('handle_trap();\nhandle_fault();\nevict();\n'
                  '  do_tohost(code);\n  while (1);\n'
                  '__builtin___clear_cache(0,0);\nfssr x0; 1:')
        after = isa_vm.adapt_exit(before)
        self.assertTrue(after.startswith('handle_trap();\nhandle_fault();\nevict();\n'))
        self.assertIn('code == 1 ? 0 : (unsigned)code', after)
        self.assertIn('asm("a7") = 93', after)
        self.assertIn('asm volatile ("fence.i"', after)
        self.assertIn('.word 0x00301073; 1:', after)

    def test_adapter_rejects_unexpected_source(self):
        with self.assertRaises(ValueError):
            isa_vm.adapt_exit('new upstream implementation')

    def test_missing_virtual_corpus_stays_in_manifest(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            names, digest = isa_vm.inventory()
            manifest = root/'common.json'
            manifest.write_text(json.dumps(dict(complete=True, testShSha256=digest,
                expected=names, records=[dict(test=n, sha256='0'*64) for n in names], suite='env/v')))
            args = ['prepare_isa_tests.py', '--data-dir', tmp, '--out-dir', str(root/'out'),
                    '--manifest', str(manifest)]
            with patch.object(sys, 'argv', args), contextlib.redirect_stdout(io.StringIO()):
                with self.assertRaises(SystemExit) as exit_status:
                    prep.main()
            self.assertEqual(exit_status.exception.code, 1)
            generated = json.loads((root/'out/manifest.json').read_text())
            self.assertEqual(generated['expected'], names)
            self.assertFalse(generated['complete'])

    def test_changed_elf_is_rejected_before_gpu_conversion(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            name = 'rv32ui-v-add'
            (root/name).write_bytes(b'different ELF')
            manifest = root/'common.json'
            manifest.write_text(json.dumps(dict(complete=True,
                testShSha256=prep.test_inventory()[1], expected=[name], suite='env/v',
                records=[dict(test=name, sha256=hashlib.sha256(b'original ELF').hexdigest())])))
            args = ['prepare_isa_tests.py', '--data-dir', tmp, '--out-dir', str(root/'out'),
                    '--manifest', str(manifest)]
            with patch.object(sys, 'argv', args):
                with self.assertRaisesRegex(ValueError, 'ELF hash mismatch'):
                    prep.main()


if __name__ == '__main__':
    unittest.main()
