from __future__ import annotations

import tempfile
import unittest
import unicodedata
from pathlib import Path

from tools.specgen.errors import ConfigurationError
from tools.specgen.util import load_json, semantic_hash


class CanonicalJsonTests(unittest.TestCase):
    def test_key_order_does_not_change_hash(self) -> None:
        self.assertEqual(
            semantic_hash({"b": 2, "a": ["中文", 1]}),
            semantic_hash({"a": ["中文", 1], "b": 2}),
        )

    def _assert_rejected(self, raw: bytes, message: str) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "bad.json"
            path.write_bytes(raw)
            with self.assertRaisesRegex(ConfigurationError, message):
                load_json(path)

    def test_duplicate_key_rejected(self) -> None:
        self._assert_rejected(b'{"a":1,"a":2}', "重复键")

    def test_nan_rejected(self) -> None:
        self._assert_rejected(b'{"a":NaN}', "非有限")

    def test_float_rejected(self) -> None:
        self._assert_rejected(b'{"a":1.5}', "浮点")

    def test_bom_rejected(self) -> None:
        self._assert_rejected(b"\xef\xbb\xbf{}", "BOM")

    def test_non_nfc_rejected(self) -> None:
        value = unicodedata.normalize("NFD", "é")
        raw = ('{"a":"' + value + '"}').encode("utf-8")
        self._assert_rejected(raw, "NFC")


if __name__ == "__main__":
    unittest.main()
