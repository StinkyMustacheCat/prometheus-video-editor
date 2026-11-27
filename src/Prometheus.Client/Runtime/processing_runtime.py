"""Device identity and resource options passed by the editor, never global settings."""
import ctypes
import os


def cpu_threads():
    return max(0, min(64, int(os.environ.get("PROMETHEUS_CPU_THREADS", "0"))))


def cuda_device_index():
    luid = os.environ.get("PROMETHEUS_PROCESSING_ADAPTER_LUID")
    if not luid:
        return 0
    driver = ctypes.WinDLL("nvcuda.dll")
    count = ctypes.c_int()
    if driver.cuInit(0) or driver.cuDeviceGetCount(ctypes.byref(count)):
        raise RuntimeError("No se pudo inicializar el controlador CUDA.")
    expected = int(luid).to_bytes(8, "little", signed=True)
    matches = []
    for index in range(count.value):
        device = ctypes.c_int()
        if driver.cuDeviceGet(ctypes.byref(device), index):
            continue
        actual = ctypes.create_string_buffer(8)
        node = ctypes.c_uint()
        if driver.cuDeviceGetLuid(actual, ctypes.byref(node), device) == 0 and actual.raw == expected:
            matches.append(index)
    if len(matches) != 1:
        raise RuntimeError("CUDA no pudo confirmar la identidad de la GPU elegida.")
    # The driver already enumerates the CUDA_VISIBLE_DEVICES subset and order.
    # This is the logical ordinal seen by Torch/CTranslate2; do not remap it again.
    return matches[0]
