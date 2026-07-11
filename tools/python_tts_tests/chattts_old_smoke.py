import argparse
import time
from pathlib import Path

import ChatTTS
import numpy as np
from scipy.io import wavfile
import torch


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--model-dir", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--text", default="")
    parser.add_argument("--text-file", default="")
    parser.add_argument("--temperature", type=float, default=0.3)
    parser.add_argument("--top-p", type=float, default=0.7)
    parser.add_argument("--top-k", type=int, default=20)
    args = parser.parse_args()

    text = args.text
    if args.text_file:
        text = Path(args.text_file).read_text(encoding="utf-8").strip()
    if not text:
        raise ValueError("Either --text or --text-file is required")

    torch.set_num_threads(4)
    started = time.perf_counter()
    chat = ChatTTS.Chat()
    ok = chat.load(source="custom", custom_path=args.model_dir, compile=False, device=torch.device("cpu"))
    loaded = time.perf_counter()
    if not ok:
        raise RuntimeError("ChatTTS model load failed")

    params_infer_code = ChatTTS.Chat.InferCodeParams(
        spk_emb=chat.sample_random_speaker(),
        temperature=args.temperature,
        top_P=args.top_p,
        top_K=args.top_k,
    )
    params_refine_text = ChatTTS.Chat.RefineTextParams(
        prompt="[oral_2][break_4]",
    )
    wavs = chat.infer(
        [text],
        params_refine_text=params_refine_text,
        params_infer_code=params_infer_code,
        use_decoder=True,
    )
    inferred = time.perf_counter()

    output = Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    audio = np.asarray(wavs[0])
    if audio.ndim > 1:
        audio = np.squeeze(audio)
    audio = np.clip(audio, -1.0, 1.0)
    wavfile.write(str(output), 24000, (audio * 32767).astype(np.int16))

    print(f"load_seconds={loaded - started:.3f}")
    print(f"infer_seconds={inferred - loaded:.3f}")
    print(f"output={output}")
    print(f"type={type(audio).__name__}")
    print(f"shape={audio.shape}")
    print(f"samples={len(audio)}")


if __name__ == "__main__":
    main()
