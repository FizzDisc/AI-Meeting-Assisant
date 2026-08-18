import unittest
from types import SimpleNamespace
from hardware import select_compute


class FakeCuda:
    def __init__(self, available: bool, memory: int = 0) -> None:
        self.available, self.memory = available, memory
    def is_available(self): return self.available
    def get_device_properties(self, index): return SimpleNamespace(total_memory=self.memory)
    def get_device_name(self, index): return "Fake NVIDIA GPU"


class HardwareSelectionTests(unittest.TestCase):
    def test_automatic_falls_back_to_cpu_with_reason(self) -> None:
        result = select_compute(SimpleNamespace(cuda=FakeCuda(False)), "automatic")
        self.assertEqual(("cpu", "int8", 2), (result["mode"], result["computeType"], result["batchSize"]))
        self.assertIn("No compatible NVIDIA", result["fallbackReason"])

    def test_prefer_cuda_falls_back_when_unavailable(self) -> None:
        result = select_compute(SimpleNamespace(cuda=FakeCuda(False)), "prefer-cuda")
        self.assertEqual("cpu", result["mode"])
        self.assertIn("fell back to CPU", result["fallbackReason"])

    def test_cuda_profile_uses_vram_for_batch_size(self) -> None:
        result = select_compute(SimpleNamespace(cuda=FakeCuda(True, 12 * 1024**3)), "automatic")
        self.assertEqual(("cuda", "float16", 16), (result["mode"], result["computeType"], result["batchSize"]))
        self.assertEqual("Fake NVIDIA GPU", result["deviceName"])

    def test_cpu_only_overrides_available_cuda(self) -> None:
        result = select_compute(SimpleNamespace(cuda=FakeCuda(True, 12 * 1024**3)), "cpu-only")
        self.assertEqual("cpu", result["mode"])
        self.assertEqual("CPU-only mode was selected.", result["fallbackReason"])


if __name__ == "__main__": unittest.main()
