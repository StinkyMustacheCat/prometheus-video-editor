"""Device-identity regression without a GPU or model installation."""
import ctypes
import importlib.util
import os
from pathlib import Path
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location(
    "processing_runtime", Path(__file__).resolve().parents[2] /
    "src/Prometheus.Client/Runtime/processing_runtime.py")
runtime = importlib.util.module_from_spec(spec)
spec.loader.exec_module(runtime)


class Driver:
    """Logical order returned by the CUDA driver after visibility filtering."""
    def cuInit(self, flags): return 0
    def cuDeviceGetCount(self, count): count._obj.value = 2; return 0
    def cuDeviceGet(self, device, index): device._obj.value = index; return 0
    def cuDeviceGetLuid(self, luid, node, device):
        luid.raw = (111 if device.value == 0 else 222).to_bytes(8, "little", signed=True)
        return 0


class ProcessingRuntimeTests(unittest.TestCase):
    def resolve(self, visible, luid="222"):
        with patch.dict(os.environ, {"CUDA_VISIBLE_DEVICES": visible,
                                    "PROMETHEUS_PROCESSING_ADAPTER_LUID": luid}), \
             patch.object(ctypes, "WinDLL", return_value=Driver(), create=True):
            return runtime.cuda_device_index()

    def test_visible_order_is_not_applied_twice(self):
        self.assertEqual(1, self.resolve("1,0"))

    def test_uuid_visibility_is_already_resolved_by_the_driver(self):
        self.assertEqual(1, self.resolve("GPU-first,GPU-second"))

    def test_a_missing_adapter_does_not_select_device_zero(self):
        with self.assertRaisesRegex(RuntimeError, "identidad"):
            self.resolve("0,1", "333")


if __name__ == "__main__": unittest.main()
