import argparse
import json

from faster_whisper import WhisperModel


def transcribe(model_path, audio_path, language, device, compute_type, progress=False):
    model = WhisperModel(model_path, device=device, compute_type=compute_type, local_files_only=True)
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
    args = parser.parse_args()
    try:
        result = transcribe(args.model, args.input, args.language, "cuda", "float16", args.progress)
    except Exception:
        result = transcribe(args.model, args.input, args.language, "cpu", "int8", args.progress)
    with open(args.output, "w", encoding="utf-8") as output:
        json.dump(result, output, ensure_ascii=False)


if __name__ == "__main__":
    main()
