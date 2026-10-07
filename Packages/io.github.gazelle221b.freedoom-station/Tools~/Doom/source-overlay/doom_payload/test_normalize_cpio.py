import stat
import unittest
from normalize_cpio import decode, encode, normalize


class CpioTests(unittest.TestCase):
    def archive(self, reverse=False):
        def entry(name, inode, mode, content, links=1):
            return name, [inode, mode, 42, 43, links, 123, len(content), 8, 1, 5, 1, len(name)+1, 0], content
        entries = [entry(b".", 90, stat.S_IFDIR|0o755, b""),
                   entry(b"a", 100, stat.S_IFREG|0o644, b"payload", 2),
                   entry(b"b", 100, stat.S_IFREG|0o644, b"", 2),
                   entry(b"console", 102, stat.S_IFCHR|0o600, b""),
                   entry(b"link", 103, stat.S_IFLNK|0o777, b"/a")]
        if reverse:
            entries[1] = entries[1][0], entries[1][1], b""
            entries[2] = entries[2][0], entries[2][1], b"payload"
            entries.reverse()
            for _, fields, _ in entries:
                fields[0] += 700
                fields[2:4] = [777, 888]
                fields[5] = 999
        entries.append((b"TRAILER!!!", [0]*11+[11, 0], b""))
        return encode(entries)

    def test_different_metadata_order_and_hardlink_owner(self):
        first, second = normalize(self.archive()), normalize(self.archive(True))
        self.assertEqual(first, second)
        self.assertEqual(normalize(first), first)
        entries = {name:(fields, body) for name, fields, body in decode(first)}
        self.assertEqual(entries[b"a"][0][0], entries[b"b"][0][0])
        self.assertEqual(entries[b"b"][1], b"payload")
        self.assertEqual(entries[b"console"][0][9:11], [5, 1])
        self.assertEqual(entries[b"link"][1], b"/a")

    def test_truncation_is_rejected(self):
        with self.assertRaises(ValueError):
            normalize(self.archive()[:130])
