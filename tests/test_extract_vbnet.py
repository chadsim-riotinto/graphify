"""Tests for VB.NET extraction via Roslyn sidecar (INT-01, TEST-02)."""
import json
from pathlib import Path
from unittest.mock import patch, MagicMock
import pytest

FIXTURES = Path(__file__).parent / "fixtures"
SIDECAR = (Path(__file__).resolve().parent.parent.parent
           / "sidecar" / "bin" / "Release" / "net10.0" / "VbNetSidecar.exe")


def _have_sidecar():
    return SIDECAR.exists()


# --- INT-01: subprocess invocation (mock) ---

def test_extract_vbnet_calls_subprocess_once(tmp_path):
    """Exactly one subprocess call regardless of file count (TEST-02)."""
    from graphify.extract import extract_vbnet
    vb1 = tmp_path / "a.vb"
    vb2 = tmp_path / "b.vb"
    vb1.write_text("Public Class A\nEnd Class\n")
    vb2.write_text("Public Class B\nEnd Class\n")
    with patch("graphify.extract.subprocess") as mock_sub:
        mock_sub.run.return_value = MagicMock(
            returncode=0,
            stdout='{"nodes":[],"edges":[],"diagnostics":[]}',
            stderr=""
        )
        extract_vbnet([vb1, vb2])
    assert mock_sub.run.call_count == 1


# --- INT-01: JSON parsing (live sidecar) ---

@pytest.mark.skipif(not _have_sidecar(), reason="Sidecar binary not built")
def test_extract_vbnet_parses_json():
    """Return dict has nodes and edges lists."""
    from graphify.extract import extract_vbnet
    vb = FIXTURES / "sample.vb"
    result = extract_vbnet([vb])
    assert "nodes" in result
    assert "edges" in result
    assert isinstance(result["nodes"], list)
    assert isinstance(result["edges"], list)
    assert len(result["nodes"]) > 0, "Expected at least one node from sample.vb"


# --- INT-01 + SIDE-11: schema validation (live sidecar) ---

@pytest.mark.skipif(not _have_sidecar(), reason="Sidecar binary not built")
def test_extract_vbnet_output_passes_schema():
    """Sidecar output passes validate_extraction()."""
    from graphify.extract import extract_vbnet
    from graphify.validate import validate_extraction
    vb = FIXTURES / "sample.vb"
    result = extract_vbnet([vb])
    errors = validate_extraction(result)
    assert errors == [], f"Schema errors: {errors}"


# --- Graceful non-zero exit ---

def test_extract_vbnet_graceful_nonzero_exit():
    """Non-zero sidecar exit returns error dict, does not raise."""
    from graphify.extract import extract_vbnet
    with patch("graphify.extract.subprocess") as mock_sub:
        mock_sub.run.return_value = MagicMock(
            returncode=1,
            stdout="",
            stderr="fatal error"
        )
        result = extract_vbnet([Path("x.vb")])
    assert result["nodes"] == []
    assert result["edges"] == []
    assert "error" in result


# --- Missing sidecar binary ---

def test_extract_vbnet_missing_sidecar():
    """Returns error dict when sidecar binary not found."""
    from graphify.extract import extract_vbnet
    with patch("graphify.extract._find_vbnet_sidecar", return_value=None):
        result = extract_vbnet([Path("x.vb")])
    assert result["nodes"] == []
    assert "error" in result
    assert "not found" in result["error"].lower()


# --- JSON decode error ---

def test_extract_vbnet_json_decode_error():
    """Invalid JSON from sidecar returns error dict."""
    from graphify.extract import extract_vbnet
    with patch("graphify.extract.subprocess") as mock_sub:
        mock_sub.run.return_value = MagicMock(
            returncode=0,
            stdout="NOT VALID JSON {{{",
            stderr=""
        )
        result = extract_vbnet([Path("x.vb")])
    assert result["nodes"] == []
    assert "error" in result
    assert "decode" in result["error"].lower()


# --- Empty stdout ---

def test_extract_vbnet_empty_stdout():
    """Empty stdout returns empty result without error."""
    from graphify.extract import extract_vbnet
    with patch("graphify.extract.subprocess") as mock_sub:
        mock_sub.run.return_value = MagicMock(
            returncode=0,
            stdout="",
            stderr=""
        )
        result = extract_vbnet([Path("x.vb")])
    assert result["nodes"] == []
    assert result["edges"] == []
    assert "error" not in result
