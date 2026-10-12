import importlib.util
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location("audit", Path(__file__).with_name("audit-mono-build.py"))
audit = importlib.util.module_from_spec(spec)
spec.loader.exec_module(audit)


def member(name, data):
    header = f"{name:<16}{0:<12}{0:<6}{0:<6}{0:<8}{len(data):<10}`\n".encode()
    return header + data + (b"\n" if len(data) % 2 else b"")


class AuditTests(unittest.TestCase):
    def test_formats(self):
        self.assertEqual(audit.object_kind(b"BC\xc0\xde"), "llvm-bitcode")
        self.assertEqual(audit.object_kind(b"\xde\xc0\x17\x0b"), "llvm-bitcode")
        self.assertEqual(audit.object_kind(b"\0asm"), "wasm-object")
        self.assertEqual(audit.object_kind(b"\x7fELF"), "elf")
        self.assertEqual(audit.object_kind(b"x"), "other")

    def test_archive_alignment_and_symbol_table(self):
        archive = b"!<arch>\n" + member("/", b"abc") + member("a.o/", b"\0asmx") + member("b.o/", b"BC\xc0\xde")
        self.assertEqual(audit.archive_kinds(archive), {"wasm-object": 1, "llvm-bitcode": 1})

    def test_bsd_extended_name(self):
        archive = b"!<arch>\n" + member("#1/5", b"longxBC\xc0\xde")
        self.assertEqual(audit.archive_kinds(archive), {"llvm-bitcode": 1})

    def test_invalid_archive(self):
        with self.assertRaises(ValueError):
            audit.archive_kinds(b"no")
        with self.assertRaises(ValueError):
            audit.archive_kinds(b"!<arch>\n" + b"x" * 60)


if __name__ == "__main__":
    unittest.main()
