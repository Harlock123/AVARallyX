# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
what the tests check; this file covers how the code is put together and the rules that keep it working.

## What this is

A maze-racing flag hunt in the style of Namco's 1980 arcade game Rally-X, in C# on .NET 10 and Avalonia 12, drawn
through Avalonia's `DrawingContext`. Race round a scrolling maze collecting ten flags while pursuit cars hunt you; smoke spins them out
but burns fuel. One self-contained executable per platform (Windows, macOS and Linux, x64 and ARM). The graphics
are drawn in code and the sounds are synthesised by `tools/make_sounds.py`.

## Commands

```bash
dotnet build AVARallyX.slnx                       # the SDK version is in global.json (10.0.400 or later)
dotnet test AVARallyX.slnx                        # every test runs headless: no window opens
dotnet run --project src/AVARallyX                # play it; --no-audio for silence
RALLY_SHOTS=$PWD/Images dotnet test tests/AVARallyX.Tests --filter ScreenshotTests   # refresh the README's pictures
RALLY_PROBE=1 dotnet test tests/AVARallyX.Tests --filter PilotProbe --logger "console;verbosity=detailed"
                                                  # how the computer driver does (for tuning)
python3 tools/make_sounds.py                      # rebuild src/AVARallyX/Sounds (standard-library Python only)
```

`Directory.Build.props` sets `TreatWarningsAsErrors`, nullable reference types and implicit usings for every
project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVARallyX/Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title, attract, play, pause, initials, high scores), the phases of a round and
    the fixed-step loop; `GameEngine.Play.cs` plays a round: the car, pursuers, flags, rocks, smoke and fuel.
  - `Course.cs` cuts each round's maze of streets from the engine's random source, and finds distances through it.
  - `Entities.cs` has `Controls`, the `Dir` headings, the measures (`Field`), and the cars, flags and smoke.
  - `RallyPilot.cs` is the computer driver, used by the attract mode and the tests.
  - `InputHandler.cs` (keys) and `ControllerManager.cs` (SDL2 gamepads) produce `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVARallyX/Views/`: `GameCanvas` (split over partial files) draws a fixed 1024 x 768 picture scaled to the
  window, the view round the car on the left and the radar, fuel and flags on the right, and runs the 60 Hz timer.
  Ctrl+S saves a screenshot to the Pictures folder.
- `src/AVARallyX/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `tests/AVARallyX.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`: gameplay and screenshot tests, and the pilot
  probe.

## Rules that keep it working

- **Fixed steps.** `GameEngine` advances in fixed 1/60 s steps (`Field.Step`) from an accumulator, independent of
  the frame rate, so tests can play deterministically from a seed. Draw random numbers from the engine's seeded
  `_rng` (`new Random(seed)`, reset at each new game), never from `Random.Shared`.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the engine and draws it.
- **Input goes through `InputHandler`.** Keys and gamepad must produce the same `Dy` sign the engine expects
  (negative is up the screen). Test new controls through `InputHandler`, not by setting `Controls` directly.
- **Test hooks.** `GameEngine.Calm` turns off the pursuit cars and fuel use so a test can set up just what it
  wants; keep it out of normal play.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up.
- **Tests never touch the player's files.** High scores live in the app-data folder (`AVARallyX`); tests load the
  table from a fresh folder under the system temp path and run with `Silent = true`.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If you change the course or the pursuers, run the probe above to see the computer driver still gets on.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, controls, the test list) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push and pull request. It then publishes self-contained
single-file builds for win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. Every run keeps the builds as
workflow artifacts. Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a
proper release; either then asks the portfolio site to rebuild.
