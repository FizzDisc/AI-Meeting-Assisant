"""Local CTranslate2 speech inference without WhisperX/Transformers/NLTK."""
from pathlib import Path

class FasterWhisperTranscriber:
    def __init__(self, model_path, device, compute_type, language=None):
        from faster_whisper import WhisperModel, BatchedInferencePipeline
        path = Path(model_path)
        if not path.is_dir():
            raise FileNotFoundError(f"Local speech model not found: {path}")
        self.language = language
        self.model = WhisperModel(str(path), device=device, compute_type=compute_type,
                                  local_files_only=True)
        self.pipeline = BatchedInferencePipeline(model=self.model)

    def transcribe(self, audio_path, batch_size=2):
        segments, info = self.pipeline.transcribe(str(audio_path), language=self.language,
                                                  batch_size=batch_size, vad_filter=True)
        # Consume the lazy decoder before returning or caching the result.
        return {"language": info.language,
                "segments": [{"start": float(s.start), "end": float(s.end), "text": s.text}
                             for s in segments if s.text.strip()]}
