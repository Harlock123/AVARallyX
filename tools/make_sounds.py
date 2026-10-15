#!/usr/bin/env python3
"""Synthesises AVARallyX's sound effects and tunes into ../src/AVARallyX/Sounds as 16-bit mono WAV.

Nothing is sampled from any arcade machine: every effect is built here from square, triangle and
noise voices, and the tunes are new compositions. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVARallyX", "Sounds")
RNG = random.Random(1982)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # Flags: a bright chime, a double for the doubling flag, a rising run for the fuel flag.
    write("Flag.wav", cat(tone(0.06, hz("E6"), kind="pulse", release=0.4), tone(0.12, hz("A6"), kind="pulse", release=0.5)), 0.32)
    write("Special.wav", cat(*[tone(0.06, hz(n), kind="pulse", release=0.3) for n in ["C6", "E6", "G6", "C7", "G6", "C7", "E7"]]), 0.38)
    write("Lucky.wav", cat(tone(0.5, 300, 1200, "tri", release=0.3), *[tone(0.06, hz(n), kind="pulse", release=0.3) for n in ["G6", "B6", "D7"]]), 0.36)
    # The smokescreen, a pursuer spinning out.
    write("Smoke.wav", tone(0.35, 1200, 300, "noise", attack=0.02, release=0.6), 0.3)
    write("Spin.wav", tone(0.6, 900, 300, "saw", release=0.5, vibrato=0.3, vib_rate=14), 0.26)
    # Fuel warnings.
    write("LowFuel.wav", cat(*[tone(0.12, f, kind="square", release=0.2) for f in [880, 660, 880, 660]]), 0.26)
    write("Empty.wav", tone(0.8, 400, 90, "saw", release=0.4), 0.3)
    write("Bonus.wav", tone(0.04, 1500, 1700, "pulse", release=0.8), 0.18)
    write("Crash.wav", cat(mix(tone(0.6, 3000, 100, "noise", release=0.6), tone(0.5, 300, 40, "square", release=0.6, gain=0.7)),
                           *[tone(0.12, hz(n), kind="tri", release=0.4) for n in ["E5", "C5", "A4", "E4"]]), 0.48)
    write("RoundStart.wav", tune(176, [("G5", 0.5), ("G5", 0.5), ("G5", 0.5), ("D6", 1.5)], [("G2", 1), ("D3", 1), ("G2", 1)], kind="pulse"), 0.42)
    write("Clear.wav", tune(176, [("G5", 0.5), ("B5", 0.5), ("D6", 0.5), ("G6", 0.5), ("F#6", 0.5), ("D6", 0.5), ("G6", 2)], [("G2", 1), ("D3", 1), ("G2", 2)]), 0.44)
    write("ExtraLife.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.3) for n in ["G6", "B6", "D7", "G7", "D7", "G7"]]), 0.4)

    # The round's tune: a bright, bustling rally march in G (new). Loops.
    lead = [("G5", .5), ("B5", .5), ("D6", .5), ("B5", .5), ("C6", .5), ("A5", .5), ("F#5", 1),
            ("G5", .5), ("A5", .5), ("B5", .5), ("G5", .5), ("A5", .5), ("F#5", .5), ("D5", 1),
            ("E5", .5), ("G5", .5), ("C6", .5), ("E6", .5), ("D6", .5), ("B5", .5), ("G5", 1),
            ("A5", .5), ("C6", .5), ("B5", .5), ("A5", .5), ("G5", 1.5), ("-", .5),
            ("D6", .5), ("D6", .5), ("E6", .5), ("D6", .5), ("C6", .5), ("B5", .5), ("A5", 1),
            ("B5", .5), ("B5", .5), ("C6", .5), ("B5", .5), ("A5", .5), ("G5", .5), ("F#5", 1),
            ("G5", .5), ("B5", .5), ("D6", .5), ("G6", .5), ("F#6", .5), ("E6", .5), ("D6", .5), ("C6", .5),
            ("B5", .5), ("A5", .5), ("F#5", .5), ("A5", .5), ("G5", 1.5), ("-", .5)]
    bass = [("G2", .5), ("D3", .5)] * 4 + [("D3", .5), ("A3", .5)] * 4 + [("C3", .5), ("G3", .5)] * 4 + [("D3", .5), ("A3", .5), ("G2", .5), ("D3", .5)] * 2 \
         + [("D3", .5), ("A3", .5)] * 4 + [("G2", .5), ("D3", .5)] * 4 + [("C3", .5), ("G3", .5)] * 2 + [("A2", .5), ("E3", .5)] * 2 + [("D3", .5), ("A3", .5), ("G2", .5), ("D3", .5)] * 2
    write("Music.wav", tune(150, lead, bass, kind="pulse"), 0.36)

    # The title: a breezy road-trip tune (new).
    lead = [("D5", 1), ("G5", .5), ("A5", .5), ("B5", 1), ("A5", .5), ("G5", .5), ("E5", 1), ("D5", 1),
            ("E5", .5), ("G5", .5), ("C6", 1), ("B5", .5), ("A5", .5), ("B5", 2), ("-", 1),
            ("D6", 1), ("C6", .5), ("B5", .5), ("A5", 1), ("G5", .5), ("E5", .5), ("D5", 1), ("G5", 1),
            ("A5", .5), ("B5", .5), ("A5", 1), ("F#5", 1), ("G5", 3)]
    bass = [("G2", 2), ("D3", 2), ("C3", 2), ("G2", 2), ("C3", 2), ("A2", 2), ("D3", 2), ("D3", 1), ("-", 1),
            ("G2", 2), ("D3", 2), ("C3", 2), ("G2", 2), ("D3", 2), ("D3", 2), ("G2", 4)]
    write("TitleTune.wav", tune(132, lead, bass, kind="tri"), 0.45)
    write("StartTune.wav", tune(160, [("G5", 0.5), ("B5", 0.5), ("D6", 0.5), ("G6", 0.5), ("F#6", 0.5), ("D6", 0.5), ("G6", 2)], [("G2", 1), ("D3", 1), ("G2", 2)], kind="pulse"), 0.45)
    write("GameOver.wav", tune(76, [("D6", 1), ("B5", 1), ("G5", 1), ("A5", 1), ("F#5", 1), ("G5", 3)], [("G3", 2), ("E3", 2), ("D3", 2), ("G2", 2)], kind="tri"), 0.45)


if __name__ == "__main__":
    main()
