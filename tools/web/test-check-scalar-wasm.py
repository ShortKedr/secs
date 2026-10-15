import importlib.util
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("scalar_check", Path(__file__).with_name("check-scalar-wasm.py"))
module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)


class ScalarCheckTests(unittest.TestCase):
    def test_decoder_accepts_newer_runtime_patch_from_installed_workload(self):
        scratch = Path(__file__).resolve().parents[2] / "Benchmarks.Local/tmp"
        scratch.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(dir=scratch) as directory:
            root = Path(directory)
            project = root / "SpaceBattle.Web/SpaceBattle.Web.csproj"
            project.parent.mkdir()
            project.write_text("<Project><PropertyGroup><RuntimeFrameworkVersion>8.0.31</RuntimeFrameworkVersion></PropertyGroup></Project>", encoding="utf-8")
            dotnet = root / "dotnet"
            for version in ("8.0.30", "8.0.31", "8.0.32", "9.0.1"):
                decoder = dotnet / "packs/Microsoft.NET.Runtime.Emscripten.3.1.34.Sdk.linux-x64" / version / "tools/bin" / ("wasm-dis.exe" if os.name == "nt" else "wasm-dis")
                decoder.parent.mkdir(parents=True)
                decoder.touch()
            expected = dotnet / "packs/Microsoft.NET.Runtime.Emscripten.3.1.34.Sdk.linux-x64/8.0.32/tools/bin" / decoder.name
            with patch.object(module, "ROOT", root), patch.dict(os.environ, {"DOTNET_ROOT": str(dotnet)}), patch.object(module.shutil, "which", return_value=None):
                self.assertEqual(module.decoder_path(None), expected)

    def test_scalar(self):
        self.assertEqual(module.simd_lines(["(module", " (local $0 f32)", " (f32.mul (local.get $0) (f32.const 2))", ")"]), [])

    def test_vector_types_and_instructions(self):
        for line in ("(type $0 (func (param v128)))", "(local $6 v128)", "(v128.store align=8", "(f32x4.add", "(i64x2.mul"):
            self.assertEqual(len(module.simd_lines([line])), 1)

    def test_debug_data_not_code(self):
        lines = [' (data (i32.const 0) "v128.load simd_v128_ldc f32x4.add")',
                 ' (export "f32x4.add" (func $0))', ' ;; v128 metadata', ' (i32.const 0) ;; f32x4.add']
        self.assertEqual(module.simd_lines(lines), [])

    def test_escaped_quotes_and_word_boundaries(self):
        self.assertEqual(module.simd_lines(['(export "a\\\" v128 b" (func $0))', '(local $simd_v128_ldc i32)']), [])
        self.assertEqual(len(module.simd_lines(['(local $simd_v128_ldc v128)'])), 1)


if __name__ == "__main__":
    unittest.main()
