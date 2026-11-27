"""Resident SAM 2.1 video predictor. Frames and masks travel as binary stdin/stdout data."""
import argparse
from concurrent.futures import ThreadPoolExecutor
from processing_runtime import cpu_threads, cuda_device_index
from contextlib import contextmanager, nullcontext
import gc
import json
import os
import sys
from pathlib import Path

import numpy as np


def read_exact(stream, length):
    data = bytearray(length)
    view = memoryview(data)
    done = 0
    while done < length:
        received = stream.readinto(view[done:])
        if not received:
            raise EOFError("Incomplete SAM video frame")
        done += received
    return data


def respond(output, payload, data=b""):
    payload["data_length"] = len(data)
    output.write(json.dumps(payload, separators=(",", ":")).encode() + b"\n")
    if data:
        output.write(data)
    output.flush()


class PackedFrames:
    """Keep a short window as compact BGRA bytes; resize only the frame SAM requests."""

    def __init__(self, frames, width, height, image_size, device):
        import torch

        self.frames = frames
        self.width = width
        self.height = height
        self.image_size = image_size
        self.device = device
        self.mean = torch.tensor((0.485, 0.456, 0.406), device=device)[:, None, None]
        self.std = torch.tensor((0.229, 0.224, 0.225), device=device)[:, None, None]

    def __len__(self):
        return len(self.frames)

    def __getitem__(self, index):
        import torch
        import torch.nn.functional as functional

        bgra = np.frombuffer(self.frames[index], dtype=np.uint8).reshape(
            self.height, self.width, 4)
        rgb = np.ascontiguousarray(bgra[:, :, [2, 1, 0]])
        image = torch.from_numpy(rgb).to(self.device).permute(2, 0, 1)[None].float()
        image = functional.interpolate(image, size=(self.image_size, self.image_size),
                                       mode="bilinear", align_corners=False)[0] / 255.0
        return (image - self.mean) / self.std


@contextmanager
def prefetch_backbone(predictor, frames, reverse):
    """Keep only the next visual feature ready while SAM propagates the current mask."""
    import torch

    get_image_feature = predictor._get_image_feature
    encode = predictor.forward_image
    step = -1 if reverse else 1
    pending = None
    pending_index = None

    def prepare(index):
        with torch.inference_mode():
            image = frames[index].unsqueeze(0)
            return image, encode(image)

    with ThreadPoolExecutor(max_workers=1, thread_name_prefix="sam-vision") as executor:
        def get_prefetched(state, frame_idx, batch_size):
            nonlocal pending, pending_index
            if frame_idx not in state["cached_features"]:
                if pending_index != frame_idx or pending is None:
                    if pending is not None:
                        pending.cancel()
                    pending = executor.submit(prepare, frame_idx)
                state["cached_features"] = {frame_idx: pending.result()}
                pending_index = frame_idx + step
                pending = (executor.submit(prepare, pending_index)
                           if 0 <= pending_index < len(frames) else None)
            return get_image_feature(state, frame_idx, batch_size)

        predictor._get_image_feature = get_prefetched
        try:
            yield
        finally:
            predictor._get_image_feature = get_image_feature
            if pending is not None:
                pending.cancel()


def attach_onnx_encoder(predictor, root, backend):
    import torch
    from sam2_onnx import session_settings

    ort, options, providers = session_settings(root, backend)
    encoder = ort.InferenceSession(str(root / "vision_encoder.onnx"), options, providers=providers)
    expected = "DmlExecutionProvider" if backend == "directml" else "WebGpuExecutionProvider"
    if expected not in encoder.get_providers():
        raise RuntimeError(f"El codificador SAM de vídeo no activó {backend}.")
    position = predictor.image_encoder.neck.position_encoding
    no_memory = predictor.no_mem_embed.reshape(1, -1, 1, 1)

    def forward_image(image):
        outputs = encoder.run(None, {"pixel_values": image.contiguous().numpy()})
        features = [torch.from_numpy(value) for value in outputs]
        # The image export already adds the no-memory embedding. The video predictor
        # adds it only when its temporal state calls for it.
        features[-1] = features[-1] - no_memory
        return {"backbone_fpn": features, "vision_pos_enc": [position(level) for level in features]}

    predictor.forward_image = forward_image


def main(output):
    parser = argparse.ArgumentParser()
    parser.add_argument("--model-root", required=True)
    args = parser.parse_args()
    root = Path(args.model_root)
    cpu_runtime = root / "track-cpu"
    requested = os.environ.get("PROMETHEUS_SAM2_TRACK_BACKEND", "cuda").lower()
    if requested == "auto":
        raise RuntimeError("El editor debe resolver el proveedor de seguimiento antes de iniciar el trabajador.")
    if requested not in ("cuda", "torch-cpu", "directml", "webgpu"):
        raise ValueError("Proveedor SAM de vídeo desconocido.")
    if requested != "cuda" and cpu_runtime.is_dir():
        sys.path.insert(0, str(cpu_runtime))
    import torch

    sys.path.insert(0, str(root / "repo"))
    import sam2.sam2_video_predictor as video_module
    from sam2.build_sam import build_sam2_video_predictor

    device = "cuda" if requested == "cuda" else "cpu"
    if device == "cuda":
        if not torch.cuda.is_available():
            raise RuntimeError("SAM 2.1 vídeo necesita el runtime CUDA o el runtime AMD APU.")
        torch.cuda.set_device(cuda_device_index())
        if not torch.cuda.is_bf16_supported():
            raise RuntimeError("La GPU elegida no admite BF16 para el seguimiento CUDA.")
    else:
        if cpu_threads():
            torch.set_num_threads(cpu_threads())
        torch.set_num_interop_threads(1)
    autocast = torch.autocast("cuda", dtype=torch.bfloat16) if device == "cuda" else nullcontext()
    predictor = None
    with torch.inference_mode(), autocast:
        def initialize():
            result = build_sam2_video_predictor(
                "configs/sam2.1/sam2.1_hiera_s.yaml",
                str(root / "sam2.1_hiera_small.pt"), device=device, apply_postprocessing=False)
            if requested in ("directml", "webgpu"):
                attach_onnx_encoder(result, root / "onnx", requested)
            return result

        for line in sys.stdin.buffer:
            try:
                request = json.loads(line)
                if request.get("op") == "exit":
                    respond(output, {"ok": True})
                    break
                if request.get("op") == "ping":
                    if predictor is None:
                        predictor = initialize()
                    respond(output, {"ok": True, "backend": requested})
                    continue
                if request.get("op") != "track":
                    raise ValueError("Unknown SAM video operation")
                width = int(request["width"])
                height = int(request["height"])
                count = int(request["frame_count"])
                anchor = int(request["anchor_index"])
                seed_count = int(request["seed_count"])
                reverse = bool(request["reverse"])
                if (width < 1 or height < 1 or width * height > 8_000_000 or
                        count < 2 or count > 121 or anchor not in (0, count - 1) or
                        seed_count < 1 or seed_count > 16):
                    raise ValueError("Invalid SAM video window")
                seeds = [read_exact(sys.stdin.buffer, width * height)
                         for _ in range(seed_count)]
                frames = [read_exact(sys.stdin.buffer, width * height * 4)
                          for _ in range(count)]
                if predictor is None:
                    predictor = initialize()
                packed = PackedFrames(frames, width, height, predictor.image_size,
                                      predictor.device)
                pipelined = requested == "directml" and os.environ.get("PROMETHEUS_SAM2_TRACK_PIPELINE", "1") != "0"
                with prefetch_backbone(predictor, packed, reverse) if pipelined else nullcontext():
                    original_loader = video_module.load_video_frames
                    video_module.load_video_frames = lambda **_: (packed, height, width)
                    try:
                        state = predictor.init_state("memory", offload_video_to_cpu=True,
                                                     offload_state_to_cpu=True)
                    finally:
                        video_module.load_video_frames = original_loader
                    for object_id, seed in enumerate(seeds, 1):
                        predictor.add_new_mask(state, anchor, object_id,
                                               np.frombuffer(seed, dtype=np.uint8)
                                               .reshape(height, width) > 127)
                    respond(output, {"ok": True, "frame_count": count, "width": width,
                             "height": height, "backend": requested, "pipelined": pipelined})
                    produced = 0
                    for index, object_ids, logits in predictor.propagate_in_video(
                            state, start_frame_idx=anchor, max_frame_num_to_track=count,
                            reverse=reverse):
                        object_masks = []
                        for object_index in range(len(object_ids)):
                            alpha = (logits[object_index] > 0).to(torch.uint8).cpu().numpy()
                            object_masks.append(np.ascontiguousarray(alpha.squeeze() * 255))
                        respond(output, {"ok": True, "frame_index": int(index), "width": width,
                                 "height": height, "object_count": len(object_masks)},
                                b"".join(mask.tobytes() for mask in object_masks))
                        produced += 1
                    if produced != count:
                        raise RuntimeError(f"SAM video returned {produced}/{count} frames")
                    respond(output, {"ok": True, "complete": True})
                del state, packed, frames, seeds
                gc.collect()
            except Exception as exc:
                respond(output, {"ok": False, "error": str(exc)})


if __name__ == "__main__":
    # Preserve the protocol pipe while native providers send logs to stderr.
    with os.fdopen(os.dup(sys.stdout.fileno()), "wb") as output:
        os.dup2(sys.stderr.fileno(), sys.stdout.fileno())
        main(output)
