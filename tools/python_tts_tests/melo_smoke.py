import argparse
import time
from pathlib import Path

import torch
from melo.api import TTS


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", required=True)
    parser.add_argument("--text", default="")
    parser.add_argument("--text-file", default="")
    parser.add_argument("--speed", type=float, default=1.0)
    parser.add_argument("--use-hf", action="store_true")
    args = parser.parse_args()

    text = args.text
    if args.text_file:
        text = Path(args.text_file).read_text(encoding="utf-8").strip()
    if not text:
        raise ValueError("Either --text or --text-file is required")

    torch.set_num_threads(4)
    started = time.perf_counter()
    model = TTS(language="ZH", device="cpu", use_hf=args.use_hf)
    loaded = time.perf_counter()
    speaker_id = model.hps.data.spk2id["ZH"]
    output = Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    model.tts_to_file(text, speaker_id, str(output), speed=args.speed, quiet=True)
    inferred = time.perf_counter()

    print(f"load_seconds={loaded - started:.3f}")
    print(f"infer_seconds={inferred - loaded:.3f}")
    print(f"output={output}")
    print(f"speaker_id={speaker_id}")
    print(f"sample_rate={model.hps.data.sampling_rate}")


if __name__ == "__main__":
    main()
