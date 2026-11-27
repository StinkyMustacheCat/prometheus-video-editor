import argparse
import json
import sys
import os
from processing_runtime import cpu_threads, cuda_device_index

from ctranslate2 import get_cuda_device_count

from faster_whisper import WhisperModel


def transcribe(model_path, audio_path, language, device, compute_type, progress=False, cpu_threads=0, device_index=0):
    model = WhisperModel(
        model_path, device=device, compute_type=compute_type, local_files_only=True,
        cpu_threads=cpu_threads,
        device_index=device_index,
    )
    segments, info = model.transcribe(
        audio_path,
        language=None if language == "auto" else language,
        beam_size=5,
        word_timestamps=True,
        vad_filter=True,
    )
    words = []
    for segment in segments:
        if progress and info.duration > 0:
            print(f"PROMETHEUS_PROGRESS:{min(1.0, segment.end / info.duration):.5f}", flush=True)
        for word in segment.words or []:
            value = word.word.strip()
            if value and word.end > word.start:
                words.append({"start": round(word.start, 3), "end": round(word.end, 3), "text": value})
    return {"language": info.language, "device": device, "words": words}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--model", required=True)
    parser.add_argument("--language", default="auto")
    parser.add_argument("--progress", action="store_true")
    parser.add_argument("--cpu-threads", type=int, choices=range(0, 65), default=cpu_threads())
    args = parser.parse_args()

    requested = os.environ.get("PROMETHEUS_WHISPER_BACKEND", "auto")
    if requested not in ("auto", "cuda", "cpu"):
        raise RuntimeError("Proveedor Whisper desconocido.")
    has_cuda = False
    if requested != "cpu":
        try:
            has_cuda = get_cuda_device_count() > 0
        except RuntimeError:
            pass
    result = None
    if requested == "cuda" and not has_cuda:
        raise RuntimeError("Whisper no encontró un dispositivo CUDA compatible.")
    if has_cuda and requested != "cpu":
        try:
            result = transcribe(args.model, args.input, args.language, "cuda", "float16", args.progress, device_index=cuda_device_index())
        except Exception as error:
            if requested == "cuda":
                raise
            print(f"Whisper CUDA: {error}; se intentará CPU.", file=sys.stderr, flush=True)
    if result is None:
        result = transcribe(
            args.model, args.input, args.language, "cpu", "int8", args.progress,
            cpu_threads=args.cpu_threads,
        )
    with open(args.output, "w", encoding="utf-8") as output:
        json.dump(result, output, ensure_ascii=False)


if __name__ == "__main__":
    main()
