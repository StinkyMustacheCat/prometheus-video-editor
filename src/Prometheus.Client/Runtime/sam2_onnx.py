"""SAM 2.1 Small image encoder and prompt decoder."""
import sys
import os

import numpy as np
from processing_runtime import cpu_threads


def resize_axis(pixels, size, axis):
    length = pixels.shape[axis]
    if length == size:
        return pixels
    positions = (np.arange(size, dtype=np.float32) + .5) * (length / size) - .5
    indices = np.floor(positions).astype(np.intp)
    shape = [1] * pixels.ndim
    shape[axis] = size
    weights = (positions - indices.astype(np.float32)).reshape(shape)
    first = np.take(pixels, np.clip(indices, 0, length - 1), axis=axis)
    second = np.take(pixels, np.clip(indices + 1, 0, length - 1), axis=axis)
    return first + (second - first) * weights


def resize(pixels, height, width):
    return resize_axis(resize_axis(pixels, width, 1), height, 0)


def session_settings(root, requested):
    directml = root / "ort-directml"
    webgpu = root / "ort-webgpu"
    backend = requested
    if backend == "auto":
        raise RuntimeError("El editor debe resolver el proveedor SAM antes de iniciar el trabajador.")
    folder = webgpu if backend == "webgpu" or not directml.is_dir() else directml
    if not folder.is_dir():
        raise RuntimeError(f"Falta el runtime local SAM para {backend}.")
    sys.path.insert(0, str(folder))
    import onnxruntime as ort

    options = ort.SessionOptions()
    if cpu_threads():
        options.intra_op_num_threads = cpu_threads()
    options.log_severity_level = 3
    providers = ["CPUExecutionProvider"]
    if backend == "directml":
        if "DmlExecutionProvider" not in ort.get_available_providers():
            raise RuntimeError("El runtime local no incluye DirectML.")
        options.enable_mem_pattern = False
        options.execution_mode = ort.ExecutionMode.ORT_SEQUENTIAL
        providers.insert(0, ("DmlExecutionProvider", {"device_id": os.environ.get("PROMETHEUS_PROCESSING_DML_DEVICE", "0")}))
    elif backend == "webgpu":
        import onnxruntime_ep_webgpu as webgpu_ep

        ort.register_execution_provider_library("PrometheusWebGPU", webgpu_ep.get_library_path())
        vendor = int(os.environ.get("PROMETHEUS_PROCESSING_VENDOR", "4098"))
        device_id = os.environ.get("PROMETHEUS_PROCESSING_DEVICE")
        devices = [device for device in ort.get_ep_devices()
                   if device.ep_name == webgpu_ep.get_ep_name() and device.device.vendor_id == vendor
                   and (device_id is None or device.device.device_id == int(device_id))]
        if len(devices) == 1:
            options.add_provider_for_devices(devices[:1], {})
            providers = None
        elif requested != "auto":
            raise RuntimeError("WebGPU no pudo identificar de forma única la GPU elegida.")
    return ort, options, providers


class OnnxImagePredictor:
    def __init__(self, root, requested):
        ort, options, providers = session_settings(root, requested)
        self.encoder = ort.InferenceSession(str(root / "vision_encoder.onnx"), options, providers=providers)
        self.decoder = ort.InferenceSession(str(root / "prompt_encoder_mask_decoder.onnx"), options, providers=providers)
        self.features = None
        self.height = self.width = 0

    @property
    def backend(self):
        providers = set(self.encoder.get_providers()) | set(self.decoder.get_providers())
        if "DmlExecutionProvider" in providers:
            return "directml"
        if any(provider.lower().startswith("webgpu") for provider in providers):
            return "webgpu"
        return "onnx-cpu"

    def set_image(self, rgb):
        self.height, self.width = rgb.shape[:2]
        pixels = resize(rgb.astype(np.float32) / 255, 1024, 1024)
        pixels = (pixels - np.array([.485, .456, .406], np.float32)) / np.array([.229, .224, .225], np.float32)
        pixels = np.ascontiguousarray(pixels.transpose(2, 0, 1)[None, ...])
        features = self.encoder.run(None, {"pixel_values": pixels})
        self.features = {f"image_embeddings.{index}": value for index, value in enumerate(features)}

    def predict(self, points, labels):
        if self.features is None:
            raise ValueError("Prepara un fotograma antes de predecir.")
        scaled = points * np.array([1024 / self.width, 1024 / self.height], np.float32)
        inputs = {
            "input_points": scaled[None, None, ...],
            "input_labels": labels.astype(np.int64)[None, None, ...],
            "input_boxes": np.empty((1, 0, 4), np.float32),
            **self.features,
        }
        scores, logits = self.decoder.run(["iou_scores", "pred_masks"], inputs)
        best = int(np.argmax(scores[0, 0]))
        alpha = (resize(logits[0, 0, best], self.height, self.width) > 0).astype(np.uint8) * 255
        return alpha.tobytes(), float(scores[0, 0, best])
