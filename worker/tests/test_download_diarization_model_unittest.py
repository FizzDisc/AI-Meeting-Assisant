import contextlib
import io
import os
import tempfile
import unittest
from pathlib import Path
from unittest.mock import Mock, patch

from download_diarization_model import main


class DiarizationDownloadTests(unittest.TestCase):
    token = "hf_" + "a" * 24

    def invoke(self, stdin="", environment=None, extra=None, failure=False, config=True):
        with tempfile.TemporaryDirectory() as directory:
            target = Path(directory)
            if config:
                (target / "config.yaml").write_text("test", encoding="utf-8")
            download = Mock(side_effect=RuntimeError(self.token) if failure else None)
            output, error = io.StringIO(), io.StringIO()
            with patch.dict(os.environ, environment or {}, clear=True), \
                 patch("sys.stdin", io.StringIO(stdin)), \
                 patch.dict("sys.modules", {"truststore": Mock(),
                     "huggingface_hub": Mock(snapshot_download=download)}), \
                 contextlib.redirect_stdout(output), contextlib.redirect_stderr(error):
                try:
                    result = main(["--target", str(target), *(extra or [])])
                except SystemExit as exc:
                    result = exc.code
            self.assertNotIn(self.token, output.getvalue() + error.getvalue())
            return result, download

    def test_stdin_token_reaches_download_without_being_printed(self):
        result, download = self.invoke(self.token + "\r\n", extra=["--token-stdin"])
        self.assertEqual(0, result)
        self.assertEqual(self.token, download.call_args.kwargs["token"])

    def test_desktop_environment_interface_remains_supported(self):
        result, download = self.invoke(environment={"HF_TOKEN": self.token})
        self.assertEqual(0, result)
        self.assertEqual(self.token, download.call_args.kwargs["token"])

    def test_empty_or_invalid_stdin_does_not_download_or_fall_back(self):
        for value in ("", "invalid\n"):
            with self.subTest(value=value):
                result, download = self.invoke(value, {"HF_TOKEN": self.token}, ["--token-stdin"])
                self.assertEqual(1, result)
                download.assert_not_called()

    def test_legacy_secret_argument_is_rejected_without_echo(self):
        result, download = self.invoke(extra=["--token", self.token])
        self.assertEqual(2, result)
        download.assert_not_called()

    def test_download_exception_does_not_expose_token(self):
        result, _ = self.invoke(self.token, extra=["--token-stdin"], failure=True)
        self.assertEqual(1, result)

    def test_missing_pipeline_config_fails(self):
        result, _ = self.invoke(self.token, extra=["--token-stdin"], config=False)
        self.assertEqual(1, result)


if __name__ == "__main__":
    unittest.main()
