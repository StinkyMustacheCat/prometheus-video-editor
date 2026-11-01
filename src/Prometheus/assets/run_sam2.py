import argparse
import gc
import json
import os
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT / "repo"))

import numpy as np
import torch
from PIL import Image
from sam2.build_sam import build_sam2_video_predictor


MODEL_CACHE = {"kind": None, "model": None, "generator": None, "predictor": None}


def release_model():
    MODEL_CACHE.update(kind=None, model=None, generator=None, predictor=None)
    gc.collect()
    if torch.cuda.is_available():
        torch.cuda.empty_cache()


def candidate_generator(device):
    if MODEL_CACHE["kind"] != "candidates":
        release_model()
        from sam2.build_sam import build_sam2
        from sam2.automatic_mask_generator import SAM2AutomaticMaskGenerator
        model = build_sam2(
            "configs/sam2.1/sam2.1_hiera_s.yaml",
            str(ROOT / "sam2.1_hiera_small.pt"),
            device=device,
            apply_postprocessing=False,
        )
        MODEL_CACHE["model"] = model
        MODEL_CACHE["generator"] = SAM2AutomaticMaskGenerator(
            model,
            points_per_side=12 if device == "cuda" else 6,
            points_per_batch=32 if device == "cuda" else 8,
            pred_iou_thresh=0.85,
            stability_score_thresh=0.90,
            min_mask_region_area=80,
        )
        MODEL_CACHE["kind"] = "candidates"
    return MODEL_CACHE["generator"]


def video_predictor(device):
    if MODEL_CACHE["kind"] != "video":
        release_model()
        MODEL_CACHE["predictor"] = build_sam2_video_predictor(
            "configs/sam2.1/sam2.1_hiera_s.yaml",
            str(ROOT / "sam2.1_hiera_small.pt"),
            device=device,
            apply_postprocessing=False,
        )
        MODEL_CACHE["kind"] = "video"
    return MODEL_CACHE["predictor"]


def process(config):
    frames = Path(config["frames"])
    output = Path(config["output"])
    output.mkdir(parents=True, exist_ok=True)
    count = len(list(frames.glob("*.jpg")))
    if count < 1:
        raise RuntimeError("No hay fotogramas del clip")
    device = "cuda" if torch.cuda.is_available() else "cpu"
    if device == "cuda":
        torch.backends.cuda.matmul.allow_tf32 = True
        torch.backends.cudnn.allow_tf32 = True
        if torch.cuda.is_bf16_supported():
            torch.set_autocast_dtype("cuda", torch.bfloat16)
            torch.set_autocast_enabled("cuda", True)
    if config.get("candidates"):
        image = np.asarray(Image.open(frames / "000000.jpg").convert("RGB"))
        generator = candidate_generator(device)
        masks = sorted(generator.generate(image), key=lambda item: item["area"], reverse=True)
        metadata = []
        for candidate in masks[:48]:
            if candidate["area"] < image.shape[0] * image.shape[1] * 0.001:
                continue
            filename = f"candidate-{len(metadata):03d}.png"
            Image.fromarray(candidate["segmentation"].astype(np.uint8) * 255, mode="L").save(output / filename)
            overlay = np.zeros((image.shape[0], image.shape[1], 4), dtype=np.uint8)
            overlay[candidate["segmentation"]] = (255, 156, 44, 92)
            Image.fromarray(overlay, mode="RGBA").save(output / ("overlay-" + filename))
            metadata.append({"file": filename, "area": int(candidate["area"])})
        (output / "candidates.json").write_text(json.dumps(metadata), encoding="utf-8")
        print("COMPLETE", flush=True)
        return
    predictor = video_predictor(device)
    prompts = config.get("prompts", [])
    seeds = config.get("seed_masks", [])
    if not prompts and not seeds:
        raise RuntimeError("Selecciona un objeto antes de calcular la máscara")
    with torch.inference_mode():
        keep_state_on_gpu = device == "cuda" and count <= 360 and torch.cuda.mem_get_info()[0] >= 6 * 1024**3
        state = predictor.init_state(str(frames), offload_video_to_cpu=True, offload_state_to_cpu=not keep_state_on_gpu)
        used_frames = set()
        preview_logits = None
        for seed in seeds:
            idx = max(0, min(count - 1, int(seed["frame"])))
            object_id = max(1, int(seed["object_id"]))
            mask = np.asarray(Image.open(seed["path"]).convert("L")) > 127
            predictor.add_new_mask(state, frame_idx=idx, obj_id=object_id, mask=mask)
            used_frames.add((object_id, idx))
        for prompt in prompts:
            idx = max(0, min(count - 1, int(prompt["frame"])))
            object_id = max(1, int(prompt.get("object_id", 1)))
            _, _, preview_logits = predictor.add_new_points_or_box(
                state,
                frame_idx=idx,
                obj_id=object_id,
                points=np.array([[float(prompt["x"]) * state["video_width"], float(prompt["y"]) * state["video_height"]]], dtype=np.float32),
                labels=np.array([1 if prompt["positive"] else 0], dtype=np.int32),
                clear_old_points=(object_id, idx) not in used_frames,
            )
            used_frames.add((object_id, idx))
        if config.get("preview"):
            mask = (preview_logits > 0).any(dim=0).to(torch.uint8).cpu().numpy().squeeze() * 255
            Image.fromarray(mask, mode="L").save(output / "000000.png")
            print("COMPLETE", flush=True)
            return
        anchor = min(frame for _, frame in used_frames)
        completed = set()
        for reverse, start in ((False, anchor), (True, anchor)):
            for idx, object_ids, logits in predictor.propagate_in_video(state, start_frame_idx=start, reverse=reverse):
                for object_index, object_id in enumerate(object_ids):
                    object_folder = output / f"object-{int(object_id)}"
                    object_folder.mkdir(exist_ok=True)
                    object_mask = (logits[object_index] > 0).to(torch.uint8).cpu().numpy().squeeze() * 255
                    Image.fromarray(object_mask, mode="L").save(object_folder / f"{idx:06d}.png")
                mask = (logits > 0).any(dim=0).to(torch.uint8).cpu().numpy().squeeze() * 255
                Image.fromarray(mask, mode="L").save(output / f"{idx:06d}.png")
                completed.add(idx)
                print(f"PROGRESS {len(completed)} {count} {idx}", flush=True)
        if len(completed) != count:
            raise RuntimeError(f"SAM2 devolvió {len(completed)} de {count} fotogramas")
    print("COMPLETE", flush=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--config")
    parser.add_argument("--server", action="store_true")
    args = parser.parse_args()
    if args.server:
        for line in sys.stdin:
            try:
                request = json.loads(line)
                if request.get("exit"):
                    break
                process(json.loads(Path(request["config"]).read_text(encoding="utf-8")))
            except Exception as exc:
                print("ERROR " + json.dumps(str(exc), ensure_ascii=False), flush=True)
        release_model()
    elif args.config:
        process(json.loads(Path(args.config).read_text(encoding="utf-8")))
    else:
        parser.error("Se requiere --config o --server")


if __name__ == "__main__":
    main()
