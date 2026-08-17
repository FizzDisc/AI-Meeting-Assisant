"""Minimal JSON-lines boundary for the future local AI worker.

Sprint 0 deliberately does not import WhisperX, pyannote, torch, or model files.
Protocol responses go to stdout; diagnostics belong on stderr.
"""

from __future__ import annotations
import json
import sys
from typing import Any

PROTOCOL_VERSION = "1.0"


def response(request_id: str, message_type: str, payload: dict[str, Any]) -> dict[str, Any]:
    return {"protocolVersion": PROTOCOL_VERSION, "requestId": request_id, "type": message_type,
            "ok": True, "payload": payload, "error": None}


def error(request_id: str, code: str, message: str) -> dict[str, Any]:
    return {"protocolVersion": PROTOCOL_VERSION, "requestId": request_id, "type": "error",
            "ok": False, "payload": {}, "error": {"code": code, "message": message}}


def handle(message: dict[str, Any]) -> dict[str, Any]:
    request_id = str(message.get("requestId", "unknown"))
    if message.get("protocolVersion") != PROTOCOL_VERSION:
        return error(request_id, "unsupported_protocol", "Expected protocol version 1.0")
    if message.get("type") == "health.check":
        return response(request_id, "health.result", {"status": "ready", "capabilities": []})
    return error(request_id, "unsupported_request", "Only health.check is available in Sprint 0")


def main() -> int:
    for line in sys.stdin:
        try:
            result = handle(json.loads(line))
        except (json.JSONDecodeError, TypeError, ValueError) as exc:
            result = error("unknown", "invalid_request", str(exc))
        print(json.dumps(result, separators=(",", ":")), flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

