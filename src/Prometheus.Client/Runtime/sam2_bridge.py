"""Resident SAM 2.1 image predictor. Control messages are JSON lines; pixels stay binary."""
import argparse
import json
import sys
from pathlib import Path

import numpy as np
import torch


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


def respond(payload, data=b""):
    payload["data_length"] = len(data)
    sys.stdout.buffer.write(json.dumps(payload, separators=(",", ":")).encode() + b"\n")
    if data:
        sys.stdout.buffer.write(data)
    sys.stdout.buffer.flush()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--model-root", required=True)
    args = parser.parse_args()
    root = Path(args.model_root)
    sys.path.insert(0, str(root / "repo"))
    from sam2.build_sam import build_sam2
    from sam2.sam2_image_predictor import SAM2ImagePredictor

    if not torch.cuda.is_available():
        raise RuntimeError("SAM 2.1 requires CUDA on this PC")
    torch.cuda.set_device(0)
    with torch.inference_mode(), torch.autocast("cuda", dtype=torch.bfloat16):
        model = build_sam2("configs/sam2.1/sam2.1_hiera_s.yaml",
                           str(root / "sam2.1_hiera_small.pt"),
                           device="cuda", apply_postprocessing=False)
        predictor = SAM2ImagePredictor(model)
        width = height = 0
        for line in sys.stdin.buffer:
            try:
                request = json.loads(line)
                operation = request["op"]
                if operation == "ping":
                    respond({"ok": True})
                elif operation == "prepare":
                    width = int(request["width"])
                    height = int(request["height"])
                    length = int(request["data_length"])
                    if width < 1 or height < 1 or width * height * 4 != length or length > 32_000_000:
                        raise ValueError("Invalid BGRA frame size")
                    frame = np.frombuffer(read_exact(sys.stdin.buffer, length),
                                          dtype=np.uint8).reshape(height, width, 4)
                    predictor.set_image(np.ascontiguousarray(frame[:, :, [2, 1, 0]]))
                    respond({"ok": True, "width": width, "height": height})
                elif operation == "predict":
                    if not width or not height:
                        raise ValueError("Prepare a frame first")
                    points = np.asarray(request["points"], dtype=np.float32)
                    labels = np.asarray(request["labels"], dtype=np.int32)
                    if points.ndim != 2 or points.shape[1] != 2 or len(points) != len(labels):
                        raise ValueError("Invalid prompts")
                    masks, scores, _ = predictor.predict(
                        point_coords=points, point_labels=labels, multimask_output=True)
                    selected = np.ascontiguousarray(masks[int(np.argmax(scores))].astype(np.uint8) * 255)
                    respond({"ok": True, "width": width, "height": height,
                             "score": float(np.max(scores))}, selected.tobytes())
                elif operation == "exit":
                    respond({"ok": True})
                    break
                else:
                    raise ValueError("Unknown operation")
            except Exception as exc:
                respond({"ok": False, "error": str(exc)})


if __name__ == "__main__":
    main()
