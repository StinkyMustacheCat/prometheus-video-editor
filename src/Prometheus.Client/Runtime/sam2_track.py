"""Resident SAM 2.1 video predictor. Frames and masks travel as binary stdin/stdout data."""
import argparse
import gc
import json
import sys
from pathlib import Path

import numpy as np
import torch
import torch.nn.functional as functional


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


def respond(payload, data=b""):
    payload["data_length"] = len(data)
    sys.stdout.buffer.write(json.dumps(payload, separators=(",", ":")).encode() + b"\n")
    if data:
        sys.stdout.buffer.write(data)
    sys.stdout.buffer.flush()


class PackedFrames:
    """Keep a short window as compact BGRA bytes; resize only the frame SAM requests."""

    def __init__(self, frames, width, height, image_size, device):
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
        bgra = np.frombuffer(self.frames[index], dtype=np.uint8).reshape(
            self.height, self.width, 4)
        rgb = np.ascontiguousarray(bgra[:, :, [2, 1, 0]])
        image = torch.from_numpy(rgb).to(self.device).permute(2, 0, 1)[None].float()
        image = functional.interpolate(image, size=(self.image_size, self.image_size),
                                       mode="bilinear", align_corners=False)[0] / 255.0
        return (image - self.mean) / self.std


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--model-root", required=True)
    args = parser.parse_args()
    root = Path(args.model_root)
    sys.path.insert(0, str(root / "repo"))
    import sam2.sam2_video_predictor as video_module
    from sam2.build_sam import build_sam2_video_predictor

    if not torch.cuda.is_available():
        raise RuntimeError("SAM 2.1 video requires CUDA on this PC")
    torch.cuda.set_device(0)
    predictor = None
    with torch.inference_mode(), torch.autocast("cuda", dtype=torch.bfloat16):
        for line in sys.stdin.buffer:
            try:
                request = json.loads(line)
                if request.get("op") == "exit":
                    respond({"ok": True})
                    break
                if request.get("op") == "ping":
                    respond({"ok": True})
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
                    predictor = build_sam2_video_predictor(
                        "configs/sam2.1/sam2.1_hiera_s.yaml",
                        str(root / "sam2.1_hiera_small.pt"),
                        device="cuda", apply_postprocessing=False)
                packed = PackedFrames(frames, width, height, predictor.image_size,
                                      predictor.device)
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
                respond({"ok": True, "frame_count": count, "width": width,
                         "height": height})
                produced = 0
                for index, object_ids, logits in predictor.propagate_in_video(
                        state, start_frame_idx=anchor, max_frame_num_to_track=count,
                        reverse=reverse):
                    object_masks = []
                    for object_index in range(len(object_ids)):
                        alpha = (logits[object_index] > 0).to(torch.uint8).cpu().numpy()
                        object_masks.append(np.ascontiguousarray(alpha.squeeze() * 255))
                    respond({"ok": True, "frame_index": int(index), "width": width,
                             "height": height, "object_count": len(object_masks)},
                            b"".join(mask.tobytes() for mask in object_masks))
                    produced += 1
                if produced != count:
                    raise RuntimeError(f"SAM video returned {produced}/{count} frames")
                respond({"ok": True, "complete": True})
                del state, packed, frames, seeds
                gc.collect()
            except Exception as exc:
                respond({"ok": False, "error": str(exc)})


if __name__ == "__main__":
    main()
