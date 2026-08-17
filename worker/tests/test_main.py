from main import handle


def test_health_check_reports_ready() -> None:
    result = handle({"protocolVersion": "1.0", "requestId": "test-1", "type": "health.check", "payload": {}})
    assert result["ok"] is True
    assert result["payload"]["status"] == "ready"


def test_unknown_protocol_is_rejected() -> None:
    result = handle({"protocolVersion": "2.0", "requestId": "test-2", "type": "health.check", "payload": {}})
    assert result["ok"] is False
    assert result["error"]["code"] == "unsupported_protocol"

