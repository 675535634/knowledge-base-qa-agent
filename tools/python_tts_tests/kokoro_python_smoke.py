import argparse
import time
from pathlib import Path

import numpy as np
from scipy.io import wavfile

from pykokoro import GenerationConfig, PipelineConfig, build_pipeline


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--model", required=True)
    parser.add_argument("--voices", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--text", default="")
    parser.add_argument("--text-file", default="")
    parser.add_argument("--voice", default="zf_003")
    parser.add_argument("--lang", default="zh")
    parser.add_argument("--speed", type=float, default=1.0)
    args = parser.parse_args()

    text = args.text
    if args.text_file:
        text = Path(args.text_file).read_text(encoding="utf-8").strip()
    if not text:
        raise ValueError("Either --text or --text-file is required")

    started = time.perf_counter()
    pipeline = build_pipeline(
        config=PipelineConfig(
            model_path=args.model,
            voices_path=args.voices,
            model_source="local",
            voice=args.voice,
            generation=GenerationConfig(lang=args.lang, speed=args.speed),
            provider="cpu",
        ),
        eager=True,
    )
    loaded = time.perf_counter()
    result = pipeline.run(text)
    inferred = time.perf_counter()
    output = Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    audio = np.asarray(result.audio)
    if audio.dtype != np.int16:
        audio = np.clip(audio, -1.0, 1.0)
        audio = (audio * 32767).astype(np.int16)
    wavfile.write(str(output), result.sample_rate, audio)

    print(f"load_seconds={loaded - started:.3f}")
    print(f"infer_seconds={inferred - loaded:.3f}")
    print(f"output={output}")
    print(f"sample_rate={result.sample_rate}")
    print(f"samples={len(audio)}")


if __name__ == "__main__":
    main()
