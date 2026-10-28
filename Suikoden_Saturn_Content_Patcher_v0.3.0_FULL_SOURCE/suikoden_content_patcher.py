#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Suikoden Saturn Content Patcher v0.3.0
Reconstructed full-source edition.

Purpose
-------
Patch Suikoden I PSX disc images/mods without assuming an untouched USA image.
The tool works at ISO9660 file level and can:
- scan ISO / raw BIN / single-data-track CUE
- apply guarded binary patches to files inside the image
- append/relocate enlarged files when safe
- insert text blocks and update pointer tables from a manifest
- rebuild a new BIN/CUE copy
- preserve unrelated mod bytes

This is a modding framework. It does not execute Saturn SH-2 code on PSX and it
does not invent unknown event-command mappings. Saturn-only story/event ports
must provide verified file/offset/pointer mappings in a manifest.
"""

from __future__ import annotations

import argparse
import binascii
import dataclasses
import json
import os
import re
import shutil
import struct
import sys
import traceback
from pathlib import Path
from typing import Dict, Iterable, List, Optional, Tuple

VERSION = "0.3.0"
APP_TITLE = f"Suikoden Saturn Content Patcher v{VERSION}"

SECTOR_USER = 2048
RAW_SECTOR = 2352


# ---------------------------------------------------------------------------
# CD-ROM EDC / ECC
# ---------------------------------------------------------------------------

def _make_edc_lut() -> List[int]:
    lut = []
    for i in range(256):
        edc = i
        for _ in range(8):
            edc = (edc >> 1) ^ (0xD8018001 if (edc & 1) else 0)
        lut.append(edc & 0xFFFFFFFF)
    return lut


def _make_ecc_luts() -> Tuple[List[int], List[int]]:
    f = [0] * 256
    b = [0] * 256
    for i in range(256):
        j = (i << 1) ^ (0x11D if (i & 0x80) else 0)
        j &= 0xFF
        f[i] = j
        b[i ^ j] = i
    return f, b


_EDC_LUT = _make_edc_lut()
_ECC_F, _ECC_B = _make_ecc_luts()


def edc_compute(data: bytes, seed: int = 0) -> int:
    edc = seed & 0xFFFFFFFF
    for byte in data:
        edc = (edc >> 8) ^ _EDC_LUT[(edc ^ byte) & 0xFF]
    return edc & 0xFFFFFFFF


def ecc_compute(src: bytes, major_count: int, minor_count: int,
                major_mult: int, minor_inc: int) -> bytes:
    size = major_count * minor_count
    if len(src) < size:
        raise ValueError("ECC source is too short")
    dest = bytearray(major_count * 2)
    for major in range(major_count):
        index = (major >> 1) * major_mult + (major & 1)
        ecc_a = 0
        ecc_b = 0
        for _minor in range(minor_count):
            temp = src[index]
            index += minor_inc
            if index >= size:
                index -= size
            ecc_a ^= temp
            ecc_b ^= temp
            ecc_a = _ECC_F[ecc_a]
        ecc_a = _ECC_B[_ECC_F[ecc_a] ^ ecc_b]
        dest[major] = ecc_a
        dest[major + major_count] = ecc_a ^ ecc_b
    return bytes(dest)


def _bcd(n: int) -> int:
    return ((n // 10) << 4) | (n % 10)


def _msf_for_lba(lba: int) -> Tuple[int, int, int]:
    absolute = lba + 150
    m = absolute // (60 * 75)
    absolute %= 60 * 75
    s = absolute // 75
    f = absolute % 75
    return _bcd(m), _bcd(s), _bcd(f)


def regenerate_raw_sector(sector: bytearray, mode: str) -> None:
    """Recalculate EDC/ECC for a 2352-byte sector."""
    if len(sector) != RAW_SECTOR:
        raise ValueError("raw sector must be 2352 bytes")

    if mode == "MODE1/2352":
        sector[2064:2068] = struct.pack("<I", edc_compute(bytes(sector[:2064])))
        sector[2068:2076] = b"\x00" * 8
        p = ecc_compute(bytes(sector[12:2076]), 86, 24, 2, 86)
        sector[2076:2248] = p
        q = ecc_compute(bytes(sector[12:2248]), 52, 43, 86, 88)
        sector[2248:2352] = q
        return

    if mode == "MODE2/2352":
        # XA Mode 2 Form 1. EDC covers subheader + 2048 user bytes.
        sector[2072:2076] = struct.pack("<I", edc_compute(bytes(sector[16:2072])))
        saved = bytes(sector[12:16])
        sector[12:16] = b"\x00" * 4
        p = ecc_compute(bytes(sector[12:2076]), 86, 24, 2, 86)
        sector[2076:2248] = p
        q = ecc_compute(bytes(sector[12:2248]), 52, 43, 86, 88)
        sector[2248:2352] = q
        sector[12:16] = saved
        return

    raise ValueError(f"unsupported raw mode: {mode}")


def make_raw_sector(lba: int, user: bytes, mode: str) -> bytes:
    if len(user) != SECTOR_USER:
        raise ValueError("user sector must be 2048 bytes")
    sec = bytearray(RAW_SECTOR)
    sec[:12] = b"\x00" + b"\xFF" * 10 + b"\x00"
    m, s, f = _msf_for_lba(lba)
    sec[12:16] = bytes((m, s, f, 1 if mode == "MODE1/2352" else 2))
    if mode == "MODE1/2352":
        sec[16:2064] = user
    elif mode == "MODE2/2352":
        # XA Form 1 data subheader, repeated.
        sec[16:24] = bytes((0, 0, 0x08, 0, 0, 0, 0x08, 0))
        sec[24:2072] = user
    else:
        raise ValueError(mode)
    regenerate_raw_sector(sec, mode)
    return bytes(sec)


# ---------------------------------------------------------------------------
# CUE / disc abstraction
# ---------------------------------------------------------------------------

@dataclasses.dataclass
class CueTrack:
    number: int
    mode: str
    file_name: str
    index01: Optional[str] = None


@dataclasses.dataclass
class CueInfo:
    path: Path
    lines: List[str]
    tracks: List[CueTrack]

    @classmethod
    def load(cls, path: Path) -> "CueInfo":
        lines = path.read_text(encoding="utf-8-sig", errors="replace").splitlines()
        current_file = None
        current_track: Optional[CueTrack] = None
        tracks: List[CueTrack] = []
        for raw in lines:
            line = raw.strip()
            m = re.match(r'FILE\s+"([^"]+)"\s+(\S+)', line, re.I)
            if m:
                current_file = m.group(1)
                continue
            m = re.match(r"TRACK\s+(\d+)\s+(\S+)", line, re.I)
            if m:
                if not current_file:
                    raise ValueError("CUE TRACK appears before FILE")
                current_track = CueTrack(int(m.group(1)), m.group(2).upper(), current_file)
                tracks.append(current_track)
                continue
            m = re.match(r"INDEX\s+01\s+(\d\d:\d\d:\d\d)", line, re.I)
            if m and current_track:
                current_track.index01 = m.group(1)
        if not tracks:
            raise ValueError("No tracks found in CUE")
        return cls(path, lines, tracks)

    def data_tracks(self) -> List[CueTrack]:
        return [t for t in self.tracks if "MODE" in t.mode]

    def unique_files(self) -> List[str]:
        out = []
        for t in self.tracks:
            if t.file_name not in out:
                out.append(t.file_name)
        return out

    def write_for_output(self, out_cue: Path, out_bin_name: str) -> None:
        # This version supports rebuilding a CUE that uses one data file.
        files = self.unique_files()
        if len(files) != 1:
            raise ValueError("Multi-file CUE rebuild is not supported by v0.3.0")
        new_lines = []
        replaced = False
        for raw in self.lines:
            if re.match(r'\s*FILE\s+"[^"]+"\s+', raw, re.I):
                suffix = raw.split('"', 2)[-1]
                suffix = suffix.split(None, 1)
                file_type = suffix[1] if len(suffix) > 1 else "BINARY"
                indent = raw[:len(raw) - len(raw.lstrip())]
                new_lines.append(f'{indent}FILE "{out_bin_name}" {file_type}')
                replaced = True
            else:
                new_lines.append(raw)
        if not replaced:
            raise ValueError("Could not rewrite CUE FILE line")
        out_cue.write_text("\n".join(new_lines) + "\n", encoding="utf-8")


class DiscImage:
    """
    Random-access logical 2048-byte sector reader/writer for:
    - ISO 2048
    - raw MODE1/2352
    - raw MODE2/2352 Form1
    """
    def __init__(self, input_path: Path):
        self.input_path = input_path.resolve()
        self.cue: Optional[CueInfo] = None
        self.data_path: Path
        self.mode: str
        self.sector_size: int
        self.user_offset: int

        ext = self.input_path.suffix.lower()
        if ext == ".cue":
            self.cue = CueInfo.load(self.input_path)
            data = self.cue.data_tracks()
            if len(data) != 1:
                raise ValueError("Exactly one data track is required")
            tr = data[0]
            self.mode = tr.mode
            self.data_path = (self.input_path.parent / tr.file_name).resolve()
            if not self.data_path.exists():
                raise FileNotFoundError(self.data_path)
            self._set_mode(self.mode)
        elif ext == ".iso":
            self.data_path = self.input_path
            self.mode = "ISO/2048"
            self._set_mode(self.mode)
        elif ext == ".bin":
            self.data_path = self.input_path
            self.mode = self._detect_bin_mode()
            self._set_mode(self.mode)
        else:
            raise ValueError("Open .cue, .bin or .iso")

        self._verify_pvd()

    def _set_mode(self, mode: str) -> None:
        m = mode.upper()
        if m in ("ISO/2048", "MODE1/2048"):
            self.sector_size = 2048
            self.user_offset = 0
            self.mode = "ISO/2048"
        elif m == "MODE1/2352":
            self.sector_size = 2352
            self.user_offset = 16
            self.mode = m
        elif m == "MODE2/2352":
            self.sector_size = 2352
            self.user_offset = 24
            self.mode = m
        else:
            raise ValueError(f"Unsupported data-track mode: {mode}")

    def _detect_bin_mode(self) -> str:
        size = self.input_path.stat().st_size
        candidates = []
        if size >= (17 * 2048):
            candidates.append(("ISO/2048", 2048, 0))
        if size >= (17 * 2352):
            candidates += [
                ("MODE1/2352", 2352, 16),
                ("MODE2/2352", 2352, 24),
            ]
        with self.input_path.open("rb") as f:
            for mode, ss, off in candidates:
                f.seek(16 * ss + off)
                hdr = f.read(7)
                if len(hdr) >= 6 and hdr[1:6] == b"CD001":
                    return mode
        raise ValueError("Could not detect ISO9660 data mode in BIN")

    def _verify_pvd(self) -> None:
        pvd = self.read_sector(16)
        if pvd[0] != 1 or pvd[1:6] != b"CD001":
            raise ValueError("Primary Volume Descriptor not found at logical sector 16")

    def logical_sector_count_from_file(self) -> int:
        return self.data_path.stat().st_size // self.sector_size

    def read_sector(self, lba: int, path: Optional[Path] = None) -> bytes:
        p = path or self.data_path
        with p.open("rb") as f:
            f.seek(lba * self.sector_size + self.user_offset)
            data = f.read(2048)
        if len(data) != 2048:
            raise EOFError(f"sector {lba} outside image")
        return data

    def write_sector(self, out_path: Path, lba: int, user: bytes) -> None:
        if len(user) != 2048:
            raise ValueError("sector write must be exactly 2048 bytes")
        with out_path.open("r+b") as f:
            base = lba * self.sector_size
            if self.sector_size == 2048:
                f.seek(base)
                f.write(user)
                return
            f.seek(base)
            sec = bytearray(f.read(2352))
            if len(sec) != 2352:
                raise EOFError("raw sector does not exist")
            sec[self.user_offset:self.user_offset + 2048] = user
            regenerate_raw_sector(sec, self.mode)
            f.seek(base)
            f.write(sec)

    def append_user_sector(self, out_path: Path, lba: int, user: bytes) -> None:
        with out_path.open("ab") as f:
            if self.sector_size == 2048:
                f.write(user)
            else:
                f.write(make_raw_sector(lba, user, self.mode))

    def copy_to(self, out_path: Path) -> None:
        out_path.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(self.data_path, out_path)


# ---------------------------------------------------------------------------
# ISO9660 filesystem
# ---------------------------------------------------------------------------

@dataclasses.dataclass
class IsoEntry:
    path: str
    extent: int
    size: int
    flags: int
    dirrec_lba: int
    dirrec_offset: int
    record_length: int

    @property
    def is_dir(self) -> bool:
        return bool(self.flags & 0x02)


def _both_endian_u32(value: int) -> bytes:
    return struct.pack("<I", value) + struct.pack(">I", value)


class Iso9660:
    def __init__(self, disc: DiscImage, source_path: Optional[Path] = None):
        self.disc = disc
        self.source_path = source_path or disc.data_path
        self.pvd = disc.read_sector(16, self.source_path)
        self.volume_space = struct.unpack_from("<I", self.pvd, 80)[0]
        self.entries: Dict[str, IsoEntry] = {}
        self._scan()

    def _read_sector(self, lba: int) -> bytes:
        return self.disc.read_sector(lba, self.source_path)

    def read_extent(self, extent: int, size: int) -> bytes:
        out = bytearray()
        remaining = size
        lba = extent
        while remaining > 0:
            sec = self._read_sector(lba)
            take = min(remaining, 2048)
            out += sec[:take]
            remaining -= take
            lba += 1
        return bytes(out)

    def _scan(self) -> None:
        root = self.pvd[156:190]
        if not root or root[0] < 34:
            raise ValueError("Invalid ISO9660 root directory record")
        extent = struct.unpack_from("<I", root, 2)[0]
        size = struct.unpack_from("<I", root, 10)[0]
        self._scan_dir(extent, size, "")

    def _scan_dir(self, extent: int, size: int, parent: str) -> None:
        data = self.read_extent(extent, size)
        pos = 0
        children: List[IsoEntry] = []
        while pos < len(data):
            rec_len = data[pos]
            if rec_len == 0:
                pos = ((pos // 2048) + 1) * 2048
                continue
            if pos + rec_len > len(data):
                break
            rec = data[pos:pos + rec_len]
            if len(rec) < 34:
                pos += rec_len
                continue
            name_len = rec[32]
            name_raw = rec[33:33 + name_len]
            if name_raw in (b"\x00", b"\x01"):
                pos += rec_len
                continue
            name = name_raw.decode("ascii", errors="replace")
            if ";" in name:
                name = name.split(";", 1)[0]
            child_path = f"{parent}/{name}" if parent else name
            child_path = child_path.upper()
            child_extent = struct.unpack_from("<I", rec, 2)[0]
            child_size = struct.unpack_from("<I", rec, 10)[0]
            flags = rec[25]
            entry = IsoEntry(
                child_path, child_extent, child_size, flags,
                extent + (pos // 2048), pos % 2048, rec_len
            )
            self.entries[child_path] = entry
            children.append(entry)
            pos += rec_len

        for e in children:
            if e.is_dir:
                try:
                    self._scan_dir(e.extent, e.size, e.path)
                except Exception:
                    # Some discs contain unusual directory aliases. Keep scan usable.
                    pass

    def find(self, path: str) -> IsoEntry:
        key = path.replace("\\", "/").strip("/").upper()
        if key in self.entries:
            return self.entries[key]
        # convenience: unique basename
        base = key.rsplit("/", 1)[-1]
        matches = [e for p, e in self.entries.items() if p.rsplit("/", 1)[-1] == base]
        if len(matches) == 1:
            return matches[0]
        if not matches:
            raise KeyError(f"ISO file not found: {path}")
        raise KeyError(f"Ambiguous basename {base}; use full ISO path")

    def read_file(self, path: str) -> bytes:
        e = self.find(path)
        if e.is_dir:
            raise IsADirectoryError(path)
        return self.read_extent(e.extent, e.size)

    def _patch_dir_record(self, out_path: Path, entry: IsoEntry,
                          new_extent: int, new_size: int) -> None:
        sec = bytearray(self.disc.read_sector(entry.dirrec_lba, out_path))
        off = entry.dirrec_offset
        sec[off + 2: off + 10] = _both_endian_u32(new_extent)
        sec[off + 10: off + 18] = _both_endian_u32(new_size)
        self.disc.write_sector(out_path, entry.dirrec_lba, bytes(sec))

    def _update_volume_space(self, out_path: Path, new_space: int) -> None:
        # Update all standard volume descriptors until terminator.
        lba = 16
        while True:
            sec = bytearray(self.disc.read_sector(lba, out_path))
            if sec[1:6] != b"CD001":
                break
            typ = sec[0]
            if typ in (1, 2):
                sec[80:88] = _both_endian_u32(new_space)
                self.disc.write_sector(out_path, lba, bytes(sec))
            if typ == 255:
                break
            lba += 1
            if lba > 64:
                break

    def replace_file(self, out_path: Path, path: str, new_data: bytes,
                     allow_relocate: bool = True) -> Tuple[int, int, int, int]:
        """
        Returns old_extent, old_size, new_extent, new_size.
        """
        entry = self.find(path)
        if entry.is_dir:
            raise IsADirectoryError(path)
        old_sectors = (entry.size + 2047) // 2048
        new_sectors = (len(new_data) + 2047) // 2048

        if new_sectors <= old_sectors:
            new_extent = entry.extent
        else:
            if not allow_relocate:
                raise ValueError(
                    f"{path} grows from {entry.size} to {len(new_data)} bytes; relocation disabled"
                )
            new_extent = max(self.volume_space, self.disc.logical_sector_count_from_file())

        padded = new_data + b"\x00" * (new_sectors * 2048 - len(new_data))

        if new_extent == entry.extent:
            for i in range(old_sectors):
                chunk = padded[i * 2048:(i + 1) * 2048]
                if i >= new_sectors:
                    chunk = b"\x00" * 2048
                self.disc.write_sector(out_path, new_extent + i, chunk)
        else:
            # Relocation is only safe when the image is a single data-file layout.
            if self.disc.cue and len(self.disc.cue.unique_files()) != 1:
                raise ValueError("Cannot relocate files in a multi-file CUE")
            current_count = out_path.stat().st_size // self.disc.sector_size
            if current_count < new_extent:
                for lba in range(current_count, new_extent):
                    self.disc.append_user_sector(out_path, lba, b"\x00" * 2048)
            for i in range(new_sectors):
                lba = new_extent + i
                if lba < out_path.stat().st_size // self.disc.sector_size:
                    self.disc.write_sector(out_path, lba, padded[i*2048:(i+1)*2048])
                else:
                    self.disc.append_user_sector(out_path, lba, padded[i*2048:(i+1)*2048])
            new_space = max(self.volume_space, new_extent + new_sectors)
            self._update_volume_space(out_path, new_space)
            self.volume_space = new_space

        self._patch_dir_record(out_path, entry, new_extent, len(new_data))
        return entry.extent, entry.size, new_extent, len(new_data)


# ---------------------------------------------------------------------------
# Patch manifests
# ---------------------------------------------------------------------------

def crc32_hex(data: bytes) -> str:
    return f"{binascii.crc32(data) & 0xFFFFFFFF:08X}"


def parse_hex(s: str) -> bytes:
    s = re.sub(r"[^0-9A-Fa-f]", "", s or "")
    if len(s) % 2:
        raise ValueError("hex string has odd length")
    return bytes.fromhex(s)


def encode_text(text: str, spec: dict, base_dir: Path) -> bytes:
    table_file = spec.get("table_file")
    if table_file:
        table_path = (base_dir / table_file).resolve()
        mapping = json.loads(table_path.read_text(encoding="utf-8"))
        out = bytearray()
        for ch in text:
            if ch not in mapping:
                raise ValueError(f"character not in encoding table: {ch!r}")
            value = mapping[ch]
            if isinstance(value, int):
                out.append(value & 0xFF)
            else:
                out += parse_hex(str(value))
        return bytes(out)

    enc = spec.get("encoding", "ascii")
    errors = spec.get("errors", "strict")
    return text.encode(enc, errors=errors)


def _write_int(buf: bytearray, off: int, value: int, size: int, endian: str) -> None:
    if value < 0 or value >= (1 << (size * 8)):
        raise ValueError(f"pointer value {value} does not fit {size} bytes")
    buf[off:off+size] = value.to_bytes(size, endian)


def apply_binary_patch(file_data: bytes, patch: dict, force: bool = False) -> bytes:
    buf = bytearray(file_data)
    off = int(patch["offset"])
    replacement = parse_hex(patch["replace_hex"])
    expected_hex = patch.get("expected_hex")
    if off < 0 or off + len(replacement) > len(buf):
        raise ValueError(f"patch offset outside file: {off:#x}")
    if expected_hex:
        expected = parse_hex(expected_hex)
        actual = bytes(buf[off:off+len(expected)])
        if actual != expected and not force:
            raise ValueError(
                f"guard mismatch at {off:#x}: expected {expected.hex().upper()}, "
                f"found {actual.hex().upper()}"
            )
    buf[off:off+len(replacement)] = replacement
    return bytes(buf)


def apply_text_block(file_data: bytes, patch: dict, base_dir: Path) -> Tuple[bytes, List[dict]]:
    """
    Append messages to one native game file and update a pointer table.

    Manifest example:
      {
        "type": "text_block",
        "iso_path": "AREA/VA1.BIN",
        "alignment": 2,
        "encoding": "ascii",
        "terminator_hex": "00",
        "pointer_table": {
          "offset": 4096,
          "entry_size": 2,
          "endian": "little",
          "base": 0,
          "bias": 0
        },
        "messages": [{"id": 241, "text": "Hello"}]
      }

    pointer_value = string_offset - base + bias
    """
    buf = bytearray(file_data)
    align = max(1, int(patch.get("alignment", 1)))
    term = parse_hex(patch.get("terminator_hex", "00"))
    ptab = patch.get("pointer_table") or {}
    table_off = int(ptab["offset"])
    entry_size = int(ptab.get("entry_size", 2))
    endian = ptab.get("endian", "little")
    base = int(ptab.get("base", 0))
    bias = int(ptab.get("bias", 0))
    results = []

    for msg in patch.get("messages", []):
        while len(buf) % align:
            buf.append(0)
        string_off = len(buf)
        raw = encode_text(str(msg.get("text", "")), patch, base_dir) + term
        buf += raw

        msg_id = int(msg["id"])
        ptr_off = table_off + msg_id * entry_size
        if ptr_off < 0 or ptr_off + entry_size > len(file_data):
            raise ValueError(
                f"message {msg_id}: pointer entry {ptr_off:#x} is outside original file"
            )
        value = string_off - base + bias
        _write_int(buf, ptr_off, value, entry_size, endian)
        results.append({
            "id": msg_id,
            "text": msg.get("text", ""),
            "string_offset": string_off,
            "pointer_offset": ptr_off,
            "pointer_value": value,
        })

    return bytes(buf), results


def load_manifest(path: Path) -> dict:
    obj = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(obj, dict):
        raise ValueError("manifest root must be an object")
    return obj


# ---------------------------------------------------------------------------
# Optional game executable table repair
# ---------------------------------------------------------------------------

def patch_executable_file_refs(exe: bytes, relocations: List[dict]) -> Tuple[bytes, List[dict]]:
    """
    Conservative heuristic:
    patch only UNIQUE little-endian <LBA, SIZE> pairs that exactly match a
    relocated ISO file's old values.

    Unknown layouts are not guessed. A manifest can still provide explicit
    binary patches for an executable when a mod uses another table format.
    """
    buf = bytearray(exe)
    changed = []
    for r in relocations:
        old_pair = struct.pack("<II", int(r["old_extent"]), int(r["old_size"]))
        new_pair = struct.pack("<II", int(r["new_extent"]), int(r["new_size"]))
        positions = []
        start = 0
        while True:
            pos = bytes(buf).find(old_pair, start)
            if pos < 0:
                break
            positions.append(pos)
            start = pos + 1
            if len(positions) > 8:
                break
        if len(positions) == 1:
            pos = positions[0]
            buf[pos:pos+8] = new_pair
            changed.append({"path": r["iso_path"], "offset": pos, "kind": "u32_lba_size"})
    return bytes(buf), changed


def find_main_executable(iso: Iso9660) -> Optional[str]:
    preferred = ["SLUS_002.92", "MAIN.EXE", "SLES_005.27", "SLPS_005.27"]
    for p in preferred:
        try:
            e = iso.find(p)
            if not e.is_dir:
                return e.path
        except Exception:
            pass
    roots = [p for p, e in iso.entries.items()
             if "/" not in p and not e.is_dir and
             (p.endswith(".EXE") or p.startswith(("SLUS_", "SLPS_", "SLES_")))]
    return sorted(roots)[0] if roots else None


# ---------------------------------------------------------------------------
# High-level build
# ---------------------------------------------------------------------------

@dataclasses.dataclass
class BuildResult:
    output_image: str
    output_cue: Optional[str]
    changes: List[dict]
    relocations: List[dict]
    executable_repairs: List[dict]
    warnings: List[str]


def build_patched_image(input_path: Path, output_path: Path,
                        manifests: List[Path], allow_relocate: bool = True,
                        force: bool = False,
                        auto_repair_exe_refs: bool = True) -> BuildResult:
    disc = DiscImage(input_path)

    if output_path.suffix.lower() == ".cue":
        out_cue = output_path
        out_bin = output_path.with_suffix(".bin")
    elif output_path.suffix.lower() == ".iso" and disc.sector_size == 2048:
        out_cue = None
        out_bin = output_path
    else:
        out_bin = output_path.with_suffix(".bin") if output_path.suffix.lower() != ".bin" else output_path
        out_cue = output_path.with_suffix(".cue") if disc.cue else None

    disc.copy_to(out_bin)
    iso = Iso9660(disc, out_bin)

    all_patches = []
    for mp in manifests:
        obj = load_manifest(mp)
        for p in obj.get("patches", []):
            q = dict(p)
            q["_manifest_dir"] = str(mp.parent.resolve())
            q["_manifest"] = str(mp)
            all_patches.append(q)

    # Group patches by ISO file so a modded file is read/rewritten once.
    by_file: Dict[str, List[dict]] = {}
    for p in all_patches:
        iso_path = p.get("iso_path")
        if not iso_path:
            raise ValueError(f"patch in {p['_manifest']} has no iso_path")
        by_file.setdefault(str(iso_path), []).append(p)

    changes = []
    relocations = []
    warnings = []

    for iso_path, patches in by_file.items():
        original = iso.read_file(iso_path)
        data = original
        detail = []
        for p in patches:
            typ = p.get("type", "binary")
            if typ == "binary":
                before = crc32_hex(data)
                data = apply_binary_patch(data, p, force=force)
                detail.append({"type": typ, "crc_before": before, "crc_after": crc32_hex(data)})
            elif typ == "text_block":
                data, rows = apply_text_block(data, p, Path(p["_manifest_dir"]))
                detail.append({"type": typ, "messages": rows})
            elif typ == "append_file":
                src = (Path(p["_manifest_dir"]) / p["source_file"]).resolve()
                payload = src.read_bytes()
                align = max(1, int(p.get("alignment", 1)))
                b = bytearray(data)
                while len(b) % align:
                    b.append(0)
                b += payload
                data = bytes(b)
                detail.append({"type": typ, "source_file": str(src), "bytes": len(payload)})
            else:
                raise ValueError(f"unknown patch type: {typ}")

        old_extent, old_size, new_extent, new_size = iso.replace_file(
            out_bin, iso_path, data, allow_relocate=allow_relocate
        )
        row = {
            "iso_path": iso.find(iso_path).path,
            "old_extent": old_extent,
            "old_size": old_size,
            "new_extent": new_extent,
            "new_size": new_size,
            "crc_before": crc32_hex(original),
            "crc_after": crc32_hex(data),
            "patches": detail,
        }
        changes.append(row)
        if new_extent != old_extent or new_size != old_size:
            relocations.append(row)

        # Rescan from output so later operations see updated records.
        iso = Iso9660(disc, out_bin)

    executable_repairs = []
    if auto_repair_exe_refs and relocations:
        iso = Iso9660(disc, out_bin)
        exe_path = find_main_executable(iso)
        if exe_path:
            exe_old = iso.read_file(exe_path)
            exe_new, repaired = patch_executable_file_refs(exe_old, relocations)
            if repaired:
                old_extent, old_size, new_extent, new_size = iso.replace_file(
                    out_bin, exe_path, exe_new, allow_relocate=allow_relocate
                )
                executable_repairs = repaired
                if new_extent != old_extent:
                    warnings.append(
                        f"Executable {exe_path} itself relocated; verify this mod on emulator/hardware."
                    )
            else:
                warnings.append(
                    "No unique <LBA,SIZE> pairs were found in the executable. "
                    "If this mod keeps its own file table, provide explicit guarded binary patches."
                )
        else:
            warnings.append("No main PSX executable was recognized for automatic file-table repair.")

    if disc.cue:
        if out_cue is None:
            out_cue = out_bin.with_suffix(".cue")
        disc.cue.write_for_output(out_cue, out_bin.name)

    # Verify PVD and every changed file can be reopened.
    verify_disc = DiscImage(out_cue if out_cue else out_bin)
    verify_iso = Iso9660(verify_disc)
    for c in changes:
        check = verify_iso.read_file(c["iso_path"])
        if crc32_hex(check) != c["crc_after"]:
            raise RuntimeError(f"verification failed for {c['iso_path']}")

    report = {
        "tool": APP_TITLE,
        "input": str(input_path),
        "output_image": str(out_bin),
        "output_cue": str(out_cue) if out_cue else None,
        "allow_relocate": allow_relocate,
        "changes": changes,
        "relocations": relocations,
        "executable_repairs": executable_repairs,
        "warnings": warnings,
    }
    report_path = out_bin.with_suffix(out_bin.suffix + ".patch-report.json")
    report_path.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")

    return BuildResult(
        str(out_bin),
        str(out_cue) if out_cue else None,
        changes,
        relocations,
        executable_repairs,
        warnings,
    )


# ---------------------------------------------------------------------------
# Scanner / CLI
# ---------------------------------------------------------------------------

def scan_image(path: Path) -> dict:
    disc = DiscImage(path)
    iso = Iso9660(disc)
    files = []
    for p in sorted(iso.entries):
        e = iso.entries[p]
        if not e.is_dir:
            files.append({
                "path": p,
                "extent": e.extent,
                "size": e.size,
            })
    return {
        "input": str(path),
        "mode": disc.mode,
        "sector_size": disc.sector_size,
        "volume_space": iso.volume_space,
        "file_count": len(files),
        "main_executable": find_main_executable(iso),
        "files": files,
    }


def cli_main(argv: Optional[List[str]] = None) -> int:
    ap = argparse.ArgumentParser(description=APP_TITLE)
    sub = ap.add_subparsers(dest="cmd")

    s = sub.add_parser("scan", help="scan a PSX image")
    s.add_argument("image")

    b = sub.add_parser("build", help="apply manifests and rebuild a copy")
    b.add_argument("image")
    b.add_argument("output")
    b.add_argument("manifest", nargs="+")
    b.add_argument("--no-relocate", action="store_true")
    b.add_argument("--force", action="store_true")
    b.add_argument("--no-exe-repair", action="store_true")

    args = ap.parse_args(argv)
    if args.cmd == "scan":
        print(json.dumps(scan_image(Path(args.image)), indent=2))
        return 0
    if args.cmd == "build":
        result = build_patched_image(
            Path(args.image), Path(args.output),
            [Path(x) for x in args.manifest],
            allow_relocate=not args.no_relocate,
            force=args.force,
            auto_repair_exe_refs=not args.no_exe_repair,
        )
        print(json.dumps(dataclasses.asdict(result), indent=2))
        return 0

    launch_gui()
    return 0


# ---------------------------------------------------------------------------
# Tkinter GUI
# ---------------------------------------------------------------------------

def launch_gui() -> None:
    import tkinter as tk
    from tkinter import filedialog, messagebox, ttk

    root = tk.Tk()
    root.title(APP_TITLE)
    root.geometry("980x680")

    input_var = tk.StringVar()
    output_var = tk.StringVar()
    manifest_var = tk.StringVar()
    relocate_var = tk.BooleanVar(value=True)
    exe_repair_var = tk.BooleanVar(value=True)
    force_var = tk.BooleanVar(value=False)

    top = ttk.Frame(root, padding=8)
    top.pack(fill="x")

    def row(label, var, browse_cb, r):
        ttk.Label(top, text=label, width=18).grid(row=r, column=0, sticky="w", pady=3)
        ttk.Entry(top, textvariable=var).grid(row=r, column=1, sticky="ew", padx=4)
        ttk.Button(top, text="Browse...", command=browse_cb).grid(row=r, column=2)
    top.columnconfigure(1, weight=1)

    def browse_input():
        p = filedialog.askopenfilename(
            title="Open Suikoden PSX image/mod",
            filetypes=[("PSX images", "*.cue *.bin *.iso"), ("All files", "*.*")]
        )
        if p:
            input_var.set(p)
            src = Path(p)
            if src.suffix.lower() == ".cue":
                output_var.set(str(src.with_name(src.stem + "_SaturnContent_v030.cue")))
            elif src.suffix.lower() == ".iso":
                output_var.set(str(src.with_name(src.stem + "_SaturnContent_v030.iso")))
            else:
                output_var.set(str(src.with_name(src.stem + "_SaturnContent_v030.bin")))

    def browse_output():
        p = filedialog.asksaveasfilename(
            title="Save rebuilt image",
            defaultextension=".bin",
            filetypes=[("BIN", "*.bin"), ("CUE", "*.cue"), ("ISO", "*.iso"), ("All files", "*.*")]
        )
        if p:
            output_var.set(p)

    def browse_manifest():
        ps = filedialog.askopenfilenames(
            title="Select patch manifest(s)",
            filetypes=[("JSON manifest", "*.json"), ("All files", "*.*")]
        )
        if ps:
            manifest_var.set(";".join(ps))

    row("PSX BIN/CUE/ISO", input_var, browse_input, 0)
    row("Output copy", output_var, browse_output, 1)
    row("Patch manifests", manifest_var, browse_manifest, 2)

    opts = ttk.Frame(root, padding=(8, 0))
    opts.pack(fill="x")
    ttk.Checkbutton(opts, text="Extend free space / relocate enlarged ISO files",
                    variable=relocate_var).pack(side="left", padx=4)
    ttk.Checkbutton(opts, text="Auto repair unique EXE <LBA,Size> references",
                    variable=exe_repair_var).pack(side="left", padx=12)
    ttk.Checkbutton(opts, text="Force guarded byte patches on mismatch",
                    variable=force_var).pack(side="left", padx=12)

    info = ttk.LabelFrame(root, text="Functions in v0.3.0", padding=8)
    info.pack(fill="x", padx=8, pady=8)
    ttk.Label(
        info,
        text=(
            "• Mod-safe ISO9660 scan   • Saturn portrait/binary manifests   "
            "• English text insertion manifests\n"
            "• Extend AREA/native files when required   • Rebuild BIN/CUE   "
            "• Verify output and write JSON report\n"
            "The tool never converts Saturn SH-2 executable code directly. "
            "Unknown Saturn CMD/event mappings must be supplied as verified patches."
        ),
        justify="left"
    ).pack(anchor="w")

    log = tk.Text(root, wrap="word", font=("Consolas", 10))
    log.pack(fill="both", expand=True, padx=8, pady=(0, 8))

    def say(msg=""):
        log.insert("end", str(msg) + "\n")
        log.see("end")
        root.update_idletasks()

    def do_scan():
        try:
            p = Path(input_var.get())
            if not p.exists():
                raise FileNotFoundError("Select an input image first")
            say(f"Scanning: {p}")
            result = scan_image(p)
            say(json.dumps({k:v for k,v in result.items() if k != "files"}, indent=2))
            say("First files:")
            for item in result["files"][:40]:
                say(f"  {item['path']:<42} LBA {item['extent']:>7}  {item['size']:>9} bytes")
            if len(result["files"]) > 40:
                say(f"  ... {len(result['files']) - 40} more")
            say("Scan OK.")
        except Exception as e:
            say("ERROR: " + str(e))
            say(traceback.format_exc())
            messagebox.showerror(APP_TITLE, str(e))

    def do_build():
        try:
            src = Path(input_var.get())
            out = Path(output_var.get())
            manifests = [Path(x) for x in manifest_var.get().split(";") if x.strip()]
            if not src.exists():
                raise FileNotFoundError("Select an input image first")
            if not manifests:
                raise ValueError("Select at least one JSON manifest")
            if src.resolve() == out.resolve():
                raise ValueError("Output must be a new file; the original is never overwritten")
            say("Building new copy...")
            result = build_patched_image(
                src, out, manifests,
                allow_relocate=relocate_var.get(),
                force=force_var.get(),
                auto_repair_exe_refs=exe_repair_var.get(),
            )
            say(f"Output image: {result.output_image}")
            if result.output_cue:
                say(f"Output CUE:   {result.output_cue}")
            say(f"Changed files: {len(result.changes)}")
            say(f"Relocated/enlarged: {len(result.relocations)}")
            say(f"EXE table repairs: {len(result.executable_repairs)}")
            for w in result.warnings:
                say("WARNING: " + w)
            say("Verification passed.")
            messagebox.showinfo(APP_TITLE, "Build complete and verified.\nOpen the output CUE when one was created.")
        except Exception as e:
            say("ERROR: " + str(e))
            say(traceback.format_exc())
            messagebox.showerror(APP_TITLE, str(e))

    buttons = ttk.Frame(root, padding=(8, 0, 8, 8))
    buttons.pack(fill="x")
    ttk.Button(buttons, text="Scan image", command=do_scan).pack(side="left")
    ttk.Button(buttons, text="Build new BIN/CUE", command=do_build).pack(side="left", padx=8)
    ttk.Button(buttons, text="Clear log", command=lambda: log.delete("1.0", "end")).pack(side="right")

    say(APP_TITLE)
    say("Open a Suikoden I PSX image/mod. The original input is never overwritten.")
    root.mainloop()


if __name__ == "__main__":
    raise SystemExit(cli_main())
