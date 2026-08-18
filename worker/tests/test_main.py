from main import handle


def test_health_check_reports_ready() -> None:
    result = handle({"protocolVersion": "1.0", "requestId": "test-1", "type": "health.check", "payload": {}})
    assert result["ok"] is True
    assert result["payload"]["status"] in ("ready", "setup-required")
    assert result["payload"]["workerVersion"] == "0.2.0"
    assert result["payload"]["capabilities"][:2] == ["health.check", "runtime.diagnostics"]
    assert isinstance(result["payload"]["runtimeSupported"], bool)
    diagnostics = result["payload"]["diagnostics"]
    assert set(diagnostics["packages"]) == {"whisperx", "torch", "pyannoteAudio"}
    assert diagnostics["compute"]["mode"] in ("cpu", "cuda")
    assert isinstance(diagnostics["missingRequirements"], list)


def test_unknown_protocol_is_rejected() -> None:
    result = handle({"protocolVersion": "2.0", "requestId": "test-2", "type": "health.check", "payload": {}})
    assert result["ok"] is False
    assert result["error"]["code"] == "unsupported_protocol"
