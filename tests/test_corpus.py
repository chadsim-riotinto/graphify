"""Phase 4 corpus regression tests (INT-04, TEST-03).

Runs the full 79-file CPR corpus through extract_vbnet() and validates
all five success criteria:
  SC1: All files produce non-empty symbol nodes (AssemblyInfo.vb exempted)
  SC2: handles_event edges present for Designer-paired frm*.vb files
  SC3: No duplicate class nodes from partial class pairs
  SC4: No cross-file node ID collisions
  SC5: No encoding errors (replacement characters) in symbol names
  TEST-03: Full corpus passes validate_extraction()
"""
from __future__ import annotations

import os
from collections import defaultdict
from pathlib import Path

import pytest

from graphify.extract import extract_vbnet, _find_vbnet_sidecar
from graphify.validate import validate_extraction


# ── Corpus location ──────────────────────────────────────────────────────────

def _corpus_dir() -> Path | None:
    """Locate Source/CPR directory relative to the graphify package."""
    here = Path(__file__).resolve().parent           # graphify/tests/
    project_root = here.parent.parent                # graphifyy_vbnet/
    candidate = project_root / "Source" / "CPR"
    if candidate.is_dir():
        return candidate
    return None


def _collect_vb_files(corpus: Path) -> list[Path]:
    """Walk corpus directory and return all .vb files sorted."""
    vb_files = sorted(corpus.rglob("*.vb"))
    return vb_files


_have_sidecar = _find_vbnet_sidecar() is not None
_corpus = _corpus_dir()
_skip_reason = (
    "VbNetSidecar binary not found"
    if not _have_sidecar
    else "Source/CPR corpus not found"
    if _corpus is None
    else None
)

pytestmark = pytest.mark.skipif(
    _skip_reason is not None,
    reason=_skip_reason or "",
)


# ── Shared fixture: run corpus once ──────────────────────────────────────────

@pytest.fixture(scope="module")
def corpus_result():
    """Extract the full corpus once and share across all tests in this module."""
    assert _corpus is not None
    vb_files = _collect_vb_files(_corpus)
    assert len(vb_files) > 0, "No .vb files found in Source/CPR"
    result = extract_vbnet(vb_files)
    assert "error" not in result, f"Sidecar error: {result.get('error')}"
    assert len(result["nodes"]) > 0, "Corpus produced zero nodes"
    return result


# ── SC1: All files produce non-empty nodes ───────────────────────────────────

ASSEMBLY_INFO_EXEMPTIONS = frozenset({"assemblyinfo.vb"})


def test_all_files_produce_non_empty_nodes(corpus_result):
    """SC1: Every corpus file produces at least one symbol node (except AssemblyInfo.vb)."""
    nodes_by_file: dict[str, list[dict]] = defaultdict(list)
    for node in corpus_result["nodes"]:
        fname = Path(node["source_file"]).name.lower()
        nodes_by_file[fname].append(node)

    zero_symbol = []
    for fname, node_list in nodes_by_file.items():
        if fname in ASSEMBLY_INFO_EXEMPTIONS:
            continue
        # Filter out the file node itself (label == filename)
        non_file = [n for n in node_list if n["label"] != Path(fname).name]
        if not non_file:
            zero_symbol.append(fname)

    assert zero_symbol == [], f"Files with zero symbol nodes: {zero_symbol}"


# ── SC2: handles_event edges for frm*.vb files ──────────────────────────────

# These 5 frm files have Designer.vb partners with WithEvents + Handles clauses
FORMS_WITH_DESIGNER_HANDLES = frozenset({
    "FrmMonthEndFilter.vb",
    "frmMonthEndSignOffInputReport.vb",
    "frmSummariseFilter.vb",
    "frmWADepositFilter.vb",
    "frmWADepositReport.vb",
})


def test_handles_event_edges_for_designer_paired_forms(corpus_result):
    """SC2: handles_event edges present for Designer-paired frm*.vb files."""
    he_by_file: dict[str, list[dict]] = defaultdict(list)
    for edge in corpus_result["edges"]:
        if edge["relation"] == "handles_event":
            he_by_file[Path(edge["source_file"]).name].append(edge)

    missing = [f for f in FORMS_WITH_DESIGNER_HANDLES if not he_by_file.get(f)]
    assert missing == [], f"Missing handles_event edges for: {missing}"


# ── SC3: No duplicate class nodes from partial pairs ────────────────────────

# The 5 Designer-paired forms whose class nodes should appear exactly once
# after MergePartialClasses() deduplication.
PARTIAL_CLASS_NAMES = frozenset({
    "FrmMonthEndFilter",
    "frmMonthEndSignOffInputReport",
    "frmSummariseFilter",
    "frmWADepositFilter",
    "frmWADepositReport",
})


def test_no_duplicate_class_nodes_from_partial_pairs(corpus_result):
    """SC3: Each partial class pair produces exactly one merged class node."""
    # Count occurrences of each partial class label at the class level
    # (nodes whose label matches the class name exactly — not child nodes)
    all_labels = [n["label"] for n in corpus_result["nodes"]]
    duplicates = []
    for class_name in PARTIAL_CLASS_NAMES:
        count = all_labels.count(class_name)
        if count != 1:
            duplicates.append(f"{class_name} appears {count} times (expected 1)")

    assert duplicates == [], (
        f"Duplicate class nodes from partial pairs: {duplicates}"
    )


# ── SC4: No cross-file node ID collisions ───────────────────────────────────

def test_no_cross_file_node_id_collisions(corpus_result):
    """SC4: Namespace-qualified IDs prevent cross-file collisions."""
    id_to_files: dict[str, set[str]] = defaultdict(set)
    for node in corpus_result["nodes"]:
        id_to_files[node["id"]].add(Path(node["source_file"]).name)

    # After merge, some IDs legitimately appear from multiple source_files
    # (Designer children remapped to code-file class ID). Filter to IDs that
    # appear from 3+ DIFFERENT files which would indicate a real collision.
    collisions = {
        nid: files
        for nid, files in id_to_files.items()
        if len(files) > 2
    }
    assert collisions == {}, f"Cross-file node ID collisions: {collisions}"


# ── SC5: No encoding errors ─────────────────────────────────────────────────

def test_no_encoding_errors(corpus_result):
    """SC5: No replacement characters in symbol names."""
    replacement_char = "\ufffd"
    bad_nodes = [
        (n["id"], n["label"], Path(n["source_file"]).name)
        for n in corpus_result["nodes"]
        if replacement_char in n["label"] or replacement_char in n["id"]
    ]
    assert bad_nodes == [], f"Nodes with replacement characters: {bad_nodes}"


# ── TEST-03: Full corpus passes validate_extraction ──────────────────────────

def test_corpus_passes_validate_extraction(corpus_result):
    """TEST-03: validate_extraction() returns 0 errors on full corpus output."""
    errors = validate_extraction(corpus_result)
    assert errors == [], (
        f"validate_extraction found {len(errors)} error(s):\n"
        + "\n".join(str(e) for e in errors[:20])
    )


# ── Corpus completeness canary ─────────────────────────────────────────────

def test_corpus_file_count():
    """Canary: update this number when corpus intentionally changes.

    WR-05: Separated from the corpus_result fixture so that a corpus size
    change does not block all other regression tests with a confusing
    fixture error. Fails independently with a clear message.
    """
    assert _corpus is not None
    vb_files = _collect_vb_files(_corpus)
    assert len(vb_files) == 79, (
        f"Corpus size changed: expected 79, found {len(vb_files)}. "
        "Update this assertion if the change is intentional."
    )
