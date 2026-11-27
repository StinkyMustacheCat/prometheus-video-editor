"""Resident SAM 2.1 image predictor. Control messages are JSON lines; pixels stay binary."""
import argparse
import json
import os
import sys
from pathlib import Path

import numpy as np
from processing_runtime import cuda_device_index


def read_exact(stream, length):
    chunks = bytearray(length)
    view = memoryview(chunks)
    received = 0
    while received < length:
        count = stream.readinto(view[received:])
        if not count:
            raise EOFError("Incomplete frame")
        received += count
    return chunks


def respond(output, payload, data=b""):
    payload["data_length"] = len(data)
    output.write(json.dumps(payload, separators=(",", ":")).encode() + b"\n")
    if data:
        output.write(data)
    output.flush()


def main(output):
    parser = argparse.ArgumentParser()
    parser.add_argument("--model-root", required=True)
    args = parser.parse_args()
    root = Path(args.model_root)
    requested = os.environ.get("PROMETHEUS_SAM2_BACKEND", "cuda").lower()
    if requested not in ("auto", "directml", "webgpu", "onnx-cpu", "cuda"):
        raise ValueError("Proveedor SAM desconocido.")
    graphs = root / "onnx"
    has_onnx = (graphs / "vision_encoder.onnx").is_file()
    if requested not in ("auto", "cuda") or requested == "auto" and has_onnx:
        from sam2_onnx import OnnxImagePredictor

        predictor = OnnxImagePredictor(graphs, requested)
        serve(output, predictor)
        return

    import torch

    sys.path.insert(0, str(root / "repo"))
    from sam2.build_sam import build_sam2
    from sam2.sam2_image_predictor import SAM2ImagePredictor

    if not torch.cuda.is_available():
        raise RuntimeError("SAM 2.1 requiere CUDA o el componente SAM ONNX local.")
    torch.cuda.set_device(cuda_device_index())
    if not torch.cuda.is_bf16_supported():
        raise RuntimeError("La GPU elegida no admite la precisión BF16 de esta ruta SAM. Usa DirectML o CPU.")
    with torch.inference_mode(), torch.autocast("cuda", dtype=torch.bfloat16):
        model = build_sam2("configs/sam2.1/sam2.1_hiera_s.yaml",
                           str(root / "sam2.1_hiera_small.pt"),
                           device="cuda", apply_postprocessing=False)
        serve(output, CudaPredictor(SAM2ImagePredictor(model)))


class CudaPredictor:
    backend = "cuda"

    def __init__(self, predictor):
        self.predictor = predictor

    def set_image(self, rgb):
        self.predictor.set_image(rgb)

    def predict(self, points, labels):
        masks, scores, _ = self.predictor.predict(
            point_coords=points, point_labels=labels, multimask_output=True)
        best = int(np.argmax(scores))
        return (masks[best].astype(np.uint8) * 255).tobytes(), float(scores[best])


def serve(output, predictor):
    width = height = 0
    for line in sys.stdin.buffer:
        try:
            request = json.loads(line)
            operation = request["op"]
            if operation == "ping":
                respond(output, {"ok": True, "backend": predictor.backend})
            elif operation == "prepare":
                width = height = 0
                received_width = int(request["width"])
                received_height = int(request["height"])
                length = int(request["data_length"])
                if received_width < 1 or received_height < 1 or received_width * received_height * 4 != length or length > 32_000_000:
                    raise ValueError("Invalid BGRA frame size")
                frame = np.frombuffer(read_exact(sys.stdin.buffer, length), dtype=np.uint8)
                frame = frame.reshape(received_height, received_width, 4)
                predictor.set_image(np.ascontiguousarray(frame[:, :, [2, 1, 0]]))
                width, height = received_width, received_height
                respond(output, {"ok": True, "width": width, "height": height, "backend": predictor.backend})
            elif operation == "predict":
                if not width or not height:
                    raise ValueError("Prepare a frame first")
                points = np.asarray(request["points"], dtype=np.float32)
                labels = np.asarray(request["labels"], dtype=np.int32)
                if (points.ndim != 2 or points.shape[1] != 2 or labels.ndim != 1
                        or len(points) != len(labels) or not 1 <= len(points) <= 32
                        or not np.isfinite(points).all() or not np.isin(labels, [0, 1]).all()
                        or not np.any(labels == 1)):
                    raise ValueError("Invalid prompts")
                data, score = predictor.predict(points, labels)
                respond(output, {"ok": True, "width": width, "height": height,
                         "score": score, "backend": predictor.backend}, data)
            elif operation == "exit":
                respond(output, {"ok": True})
                break
            else:
                raise ValueError("Unknown operation")
        except Exception as exc:
            respond(output, {"ok": False, "error": str(exc)})


if __name__ == "__main__":
    # Native providers may log to stdout. Keep the binary protocol on its own handle.
    with os.fdopen(os.dup(sys.stdout.fileno()), "wb") as output:
        os.dup2(sys.stderr.fileno(), sys.stdout.fileno())
        main(output)
