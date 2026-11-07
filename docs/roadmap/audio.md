# Audio — 0.3 Milestones

[Back to the 0.3 roadmap](../roadmap.md)

Audio workstream for Bork2d 0.3: bake, runtime, sources, listener/spatialization, Steam Audio,
tests, and the A1-A3 milestones.

Owning docs: [engine.md](../engine.md) (contracts and scene), [bake.md](../bake.md) (audio
processor and sidecar), [runtime.md](../runtime.md) (SDL output and lifecycle), and
[architecture.md](../architecture.md) (projects and threads).

## Goals

- Baked audio is a first-class asset. A source file becomes an `AudioClipAsset` plus an ADTS payload;
  no game ships WAV/FLAC/MP3 at runtime.
- A game can play 2D and 3D sounds, one-shots, looping ambience, and scene-attached emitters; it can
  set master volume, choose an output device, and select the listener camera.
- Playback is on a dedicated audio thread and cannot stall the tick or render thread. The mixer
  allocates nothing in steady state, takes no locks, and never loads or decodes on the mix block.
- Steam Audio HRTF is the 3D spatializer. A windowed app fails startup when the native audio stack
  cannot be created.
- Pong proves the whole path with impact, score, and ambience sounds.

## Architecture options

| Option | Placement | Threading | Spatializer | Cost and risk | Verdict |
| --- | --- | --- | --- | --- | --- |
| A. Contract in Engine, implementation in `Bork2d.Audio`, SDL output in Runtime | `IEngine.Audio` is possible without a cycle | Dedicated mixer thread, `SDL_QueueAudio` | Steam Audio mandatory | Matches the renderer/runtime split; native packaging and AOT are the main risks | Selected |
| B. All audio in `Bork2d.Audio` with a public surface | Engine would have to reference Audio | Same | Same | Creates an Engine/Audio cycle or a second contract project | Rejected |
| C. Audio in `Bork2d.Runtime` | No new project | SDL callback mixing | Same | Runtime becomes a monolith; the mixer is hard to test without SDL; `IEngine.Audio` still needs a contract | Rejected |
| D. New `Bork2d.Audio.Abstractions` project | Three projects | Same | Same | Cleanest layering, but a third project and dependency-matrix surface for one feature | Rejected |

The selected split is forced by the dependency direction: `IEngine<TLayer>` must return `IAudio`;
`Bork2d.Engine` must not reference `Bork2d.Audio` (Audio references Engine for `ITransform`,
`IAssets`, and camera types); and Engine must not reference Steam Audio, SharpJaad, or SDL. The
contract therefore lives in Engine, the implementation in `Bork2d.Audio`, and the SDL output seam in
Runtime, where `Silk.NET.SDL` already lives.

## Audio bake

The bake accepts WAV, AIFF, FLAC, Ogg Vorbis, MP3, and M4A/AAC sources. M4A/AAC is re-encoded and
reports a warning because it loses generation quality. The pinned FFmpeg binary decodes, resamples,
trims, and encodes; it is located by a builder option, an environment variable, or the repository
tool path, and a missing or wrong version is a bake error with an actionable message. The encoder
version, binary hash, AAC profile, ADTS writer version, and measured encoder delay enter the
incremental tool marker, so a tool change invalidates every affected clip.

The bake output is AAC-LC, ADTS, 48 kHz, mono or stereo, CBR. The sidecar sets the bitrate: mono
48-128 kbps and stereo 96-256 kbps, with defaults of 96 and 128. A value outside the range is an
error, never a silent clamp. More than two source channels is an error; the bake does not downmix
implicitly. `trim` is an optional start/end in seconds. There is no normalize, no sample-rate
override, and no loop-point metadata in 0.3.

ADTS is chosen over M4A or raw frames because it is self-describing, has no MP4 demux in the runtime,
and lets the runtime build a frame index by scanning headers at load. The 7-byte-per-frame overhead
is smaller than an index in the asset. The payload is `<base>-aac`; the asset is at `<base>`.

The asset is a generated-shape MessagePack type in `Bork2d.Engine.Assets`, like
`TextureDescriptor` and `FontAsset`:

| Field | Meaning |
| --- | --- |
| `Payload` | `DataRef` to the ADTS payload. |
| `Codec` | `AudioCodec.AacLc`, a stable integer. |
| `SampleRate` | Always `48000` in 0.3. |
| `Channels` | `1` or `2`. |
| `FrameCount` | Exact playable PCM frames per channel, after trimming and delay removal. |
| `EncodedFrameCount` | ADTS frames in the payload, for validation and index sizing. |
| `EncoderDelay` | Samples to discard at the start of the decoded stream; measured per pinned encoder build. |
| `LoadMode` | `Memory` or `Stream`, the decode strategy. |

Encoder delay is measured, not assumed: a calibration fixture encodes an impulse, decodes it with the
runtime decoder and tool version, and finds the peak. The value covers encoder priming and the
decoder's own warm-up. Guessing it produces a click at the start of every clip and breaks seamless
loops.

Sidecar example:

```toml
# Assets/audio/paddle.wav.toml
bitrate = 128        # kbps; mono 48..128, stereo 96..256
channels = "stereo"  # mono | stereo; default keeps a 1/2-channel source
mode = "memory"      # memory | stream
trim = [0.0, 0.0]    # optional [start, end] seconds
```

A missing sidecar is created with explicit defaults and an informational diagnostic. Unknown keys
are errors, as with every other sidecar. Diagnostics use `BK9400`-`BK9499`: unreadable or rejected
source, invalid sidecar option, unsupported channel count, missing or mismatched encoder, resample or
trim failure, ADTS framing failure, and the AAC-input warning.

The bake exposes an `AudioBaker` helper, mirroring `TextureBaker`: a custom processor that generates
PCM can call it and does not hand-write ADTS headers or the asset. The built-in audio processor and
Pong use the same helper.

## Runtime architecture

`Bork2d.Audio` contains:

- The mixer and voice pool.
- The clip cache, decode queue, and decode thread.
- The ADTS splitter, `SharpJaad.AAC` decoder wrapper, PCM conversion, and resampler.
- The Steam Audio context, HRTF, binaural effects, and native interop.
- The concrete `IAudio<TLayer>` implementation and the internal audio system the engine calls.
- The native-library resolver and the RID-specific Steam Audio interop.

The engine exposes an internal audio-system seam to `Bork2d.Audio` for snapshot submission,
one-shot requests, and clip-slot notification. The scene-facing `IAudioSource` node stays in Engine;
the audio assembly never reads a live transform, camera, or asset.

`Bork2d.Runtime` owns the SDL audio output: `SDL_InitSubSystem(SDL_INIT_AUDIO)`, device enumeration
and open, the null-callback device, `SDL_QueueAudio`, and `SDL_QuitSubSystem`. SDL's subsystem
reference counts are not synchronized, so every `SDL_Init*`/`SDL_Quit*` call is serialized on one
owner: the window thread in windowed mode, and a dedicated audio-platform thread in any future mode
that has output. The audio thread only calls `SDL_QueueAudio`, which SDL documents as thread-safe,
and never initializes or quits a subsystem.

Windowed startup initializes the audio subsystem on the window thread and starts the decode and mixer
threads after the platform is up and before `Setup`, so `Setup` can play. Shutdown stops the mixer,
decode, and device before platform shutdown, and the platform then quits the audio subsystem before
`SDL_Quit`. Offscreen and headless skip the whole path.

Threads:

| Thread | Owns | Audio responsibility |
| --- | --- | --- |
| Tick | `IGame`, scene mutation, snapshot capture | Capture audio sources and the listener pose into the audio snapshot; submit one-shots; never block on audio. |
| Window | SDL video, event pump | Windowed: initialize/quit the audio subsystem and open/close the device. No mixing. |
| Asset loader | Pack reads, MessagePack | Load the descriptor and payload bytes; hand off decode work. |
| Decode | AAC decode, resample, trim | Produce immutable PCM; publish clip-slot state. Never touch SDL, Steam Audio, or the scene. |
| Mixer | Voice pool, snapshot consumption, Steam Audio apply, summing, queueing | Read frozen records, mix a block, queue stereo output. No locks, allocations, logging, loads, or effect creation. |

The mixer uses a block size of 512 frames at 48 kHz. It produces stereo float32 and writes to SDL's
queue ahead of playback; a bounded queue absorbs tick jitter, and an underrun plays silence rather
than blocking. The mixer applies volume and pitch ramps over a few milliseconds, so a property change
does not click.

`SDL_QueueAudio` is chosen over callback mixing because it keeps SharpJaad, Steam Audio effect
creation, and voice lifecycle off SDL's real-time callback thread. Callback mixing is a later
optimization behind the same output seam if measured latency requires it.

## Sources and one-shots

`IAudioSource` is a scene node. It inherits `ITransform`, so its world position is its transform; it
does not add a second `Position` property that can desynchronize. It does not implement
`IRenderable<TLayer>` or `IBoundedTransform`; audio has no draw state and no culling bounds. Local
scale and rotation are ignored in 0.3 because sources are omnidirectional and have no size.

The node itself is engine-owned, like a sprite or camera node, because the scene graph and its locks
live in `Bork2d.Engine`. It captures a frozen source record and a listener record; `Bork2d.Audio`
receives those records and owns playback. This keeps live scene objects off the audio thread and
keeps decode and mixing out of the engine assembly.

`IAudio.PlayOneShot` covers fire-and-forget effects. Scene sources cover persistent emitters,
ambience, and sounds whose position must follow a transform. Both use the same voice pool, gain
staging, and spatializer.

```csharp
public interface IAudio<TLayer> where TLayer : struct, Enum
{
    float MasterVolume { get; set; }          // [0, 1]
    bool IsAvailable { get; }
    int SampleRate { get; }
    IReadOnlyList<AudioDeviceInfo> Devices { get; }
    string? Device { get; set; }              // null = system default
    ICamera<TLayer>? ListenerCamera { get; set; }

    AudioHandle PlayOneShot(AudioClipAsset clip, float volume = 1f, float pitch = 1f);
    AudioHandle PlayOneShot(
        AudioClipAsset clip,
        Vector2 worldPosition,
        float volume = 1f,
        float pitch = 1f);
    bool IsPlaying(AudioHandle handle);
    void Stop(AudioHandle handle);
    void StopAllOneShots();
}

public interface IAudioSource : ITransform
{
    AudioClipAsset? Clip { get; set; }
    AudioSpace Space { get; set; }            // TwoD | ThreeD
    float Volume { get; set; }                // [0, 1]
    float Pitch { get; set; }                 // [0.25, 4]
    bool Loop { get; set; }
    bool PlayOnStart { get; set; }
    bool IsPlaying { get; }
    bool IsPaused { get; }
    float PlaybackPosition { get; }           // Seconds; Stop resets it.

    void Play();
    void Pause();
    void Stop();
}

public enum AudioSpace
{
    TwoD,
    ThreeD,
}

public enum AudioClipLoadMode
{
    Memory,
    Stream,
}

public readonly record struct AudioDeviceInfo(string Name, bool IsDefault);
public readonly struct AudioHandle : IEquatable<AudioHandle> { /* invalid handle = default */ }
```

`AudioClipAsset`, `AudioCodec`, and `AudioClipLoadMode` live in `Bork2d.Engine.Assets`;
`IAudio`, `IAudioSource`, `AudioSpace`, `AudioDeviceInfo`, and `AudioHandle` live in
`Bork2d.Engine.Audio`.

`IEngine<TLayer>` gains `IAudio<TLayer> Audio { get; }` and
`IAudioSource CreateAudioSource(ITransform parent)`. The builder gains audio options for enabled,
device, and master volume. When audio is enabled, windowed audio is mandatory and has no fallback;
`WithAudioEnabled(false)` is the explicit opt-out for deterministic tests and tools. Offscreen and
headless runs ignore the enabled value and stay silent.

Source semantics:

| Member | Semantics |
| --- | --- |
| `Clip` | Freezes the asset's metadata once; re-assigning the same asset keeps the first frozen values. Changing it while playing stops the current voice and starts the new clip from zero if still playing. A null clip is silent and can be assigned later. |
| `Space` | `TwoD` bypasses HRTF and plays centered at the source volume; `ThreeD` uses the listener mapping and HRTF. A stereo clip is downmixed to mono for `ThreeD`. |
| `Volume` | Linear gain. Values outside `[0, 1]` or non-finite values throw `ArgumentOutOfRangeException`. |
| `Pitch` | Playback-rate multiplier. Values outside `[0.25, 4]` or non-finite values throw. Pitch changes duration; it is not time-stretch. |
| `Loop` | Repeats the whole clip at the end. There are no loop points and no crossfade in 0.3. |
| `PlayOnStart` | Starts from zero on the next snapshot when the source is rooted. Setting it while rooted starts; setting it false does not stop. Reattaching with it true restarts. |
| `Play` | Starts or resumes from the current position. A null clip sets the requested state and is silent until assigned. |
| `Pause` | Stops output, preserves position, sets `IsPaused` and clears `IsPlaying`. |
| `Stop` | Stops output, resets position, clears both flags. |
| `PlaybackPosition` | Seconds in `[0, duration]`. A natural end resets to zero. |
| Detach | A detached source is not captured or mixed. Its voice fades out and it is treated as paused: `IsPlaying == false`, `IsPaused == true`, position preserved. Reattaching does not auto-resume unless `PlayOnStart` is true or the game calls `Play`. |

A non-looping voice that reaches its end flips an atomic finished flag; `IsPlaying` observes it. A
source that is not rooted is never audible.

One-shot voices have a fixed limit. When the limit is reached, the oldest non-looping one-shot is
stolen; a scene emitter is never stolen. `AudioHandle` values are generation-checked, so a stale
handle after a steal reports `IsPlaying == false` and `Stop` is a no-op. One-shots default to `TwoD`
when no world position is supplied and `ThreeD` when one is. `PlayOneShot` freezes the asset's
metadata and reserves a clip slot before it enqueues the request, so the mixer sees only a slot id
and frozen parameters, never a live asset.

## Listener and spatialization

`IAudio.ListenerCamera` selects the listener. `null` selects the first valid camera encountered in
scene order. There is exactly one listener; multiple listeners and split-screen audio are out of
scope. If no valid camera exists, 3D sources are mixed centered and one warning is reported.

The listener pose is captured at tick end as value-only data: camera world position, camera right
and up basis, `ICamera.Size`, aspect, and a validity flag. The mixer never reads `ICamera`,
`IWindow`, or `Camera.WorldSize`; those can throw or block on the wrong thread.

`ICamera.Size` is half the visible height. The 3D mapping is a flat plane one metre in front of the
listener, not a sphere and not true world-space 3D:

```text
dx = dot(sourceWorld - cameraWorld, cameraRight)
dy = dot(sourceWorld - cameraWorld, cameraUp)

x = dx / (2 * Camera.Size)
y = dy / (2 * Camera.Size)
z = -1

direction = normalize(x, y, z)
```

The plane is one metre ahead of the listener. The visible height maps to `[-0.5, +0.5]` metres of
vertical offset; the visible width maps to `[-aspect / 2, +aspect / 2]` metres because the same
`2 * Camera.Size` divisor is used on both axes. At 16:9 the screen edge is about 0.89 metres to the
side; at 32:9 it is about 1.78 metres. This is the requested aspect-ratio behavior, and it is why a
wider window moves a source less for the same screen position.

The source is always on the plane, so its distance varies slightly from one metre at the center to
the plane edge. There is no distance attenuation and no air absorption; `Volume` is the only loudness
control. A source outside the visible rectangle remains on the plane and is still audible at its
edgeward angle; there is no off-screen mute in 0.3. A 180-degree camera rotation rotates the
right/up basis, so the pan follows the image. A mirrored camera mirrors the pan.

The direction vector is in Steam Audio's listener-local frame: `+X` right, `+Y` up, `-Z` ahead. Steam
Audio's binaural effect receives that direction with bilinear interpolation and produces stereo
output. A stereo source is downmixed to mono first; feeding two channels to one binaural effect
would smear the image. `TwoD` sources bypass the binaural effect entirely and play centered.

If the listener is invalid (`Size` non-finite or non-positive, zero or non-finite aspect, a singular
or non-unit-scale camera), `ThreeD` sources are mixed centered and one warning is logged. A minimized
window keeps its last confirmed framebuffer size, so the mapping remains valid.

This is a head-relative, screen-space HRTF mix. It is not a world-space 3D scene with front/back,
distance, occlusion, or reverb. The public enum keeps the familiar `TwoD`/`ThreeD` names, but the
documented meaning of `ThreeD` is "screen-space HRTF".

## Steam Audio

Steam Audio supplies the HRTF and binaural effect; it is not a mixer. The engine still owns voice
allocation, clip playback, resampling, gain, summing, and SDL output.

The implementation uses the maintained `SteamAudio.NET` binding and its native package, pinned to
the same SDK version. The binding hardcodes the Windows library name, so `Bork2d.Audio` installs a
`NativeLibrary` resolver that maps it to `phonon.dll`, `libphonon.so`, or `libphonon.dylib`. The
native package supplies Windows x64, Linux x64, and macOS universal binaries.

The Steam Audio surface used in 0.3 is deliberately small: context create/release, HRTF
create/release with the built-in CIPIC data, binaural effect create/apply/reset/release, audio buffer
allocation/free, and interleave. No scene, probe, simulator, occlusion, reverb, ambisonics, or SOFA
path is used. Context and HRTF creation happen once at audio startup on the mixer thread or a
control thread, never in the mix block; `iplHRTFCreate` is expensive and not thread-safe.

Native packaging is part of the deliverable: the `Bork2d.Audio` publish output carries the native
library for each supported RID, the AOT publish gate verifies it is next to the executable, and a
missing library is a startup failure in windowed mode with a message naming the RID and the missing
file. Offscreen and headless never load it. Steam Audio is Apache 2.0 with bundled third-party code;
the distribution ships its notices, including the CIPIC HRTF acknowledgment. FFmpeg is a build-host
tool, not a runtime dependency; its license and notice requirements are reviewed before the bake
tool is distributed.

The binding's delegate marshalling is an AOT risk. A startup spike creates a context, HRTF, and
binaural effect, applies one 512-frame block, and publishes with NativeAOT on each supported RID. If
the binding cannot pass that gate, the fallback is a small hand-written `LibraryImport` subset for
the same functions and SDK, not a switch to a different spatializer.

## Playback pipeline

1. Assigning `Clip` freezes the asset's metadata and reserves a clip slot. The decode thread loads
   the ADTS payload through `IAssets`, validates every header against the asset, and either decodes
   the whole clip (`memory`) or publishes a frame index and a bounded PCM ring (`stream`).
2. `memory` clips are decoded to float32, resampled to 48 kHz if needed, trimmed by `EncoderDelay`,
   and retained immutably. Several voices can share one decoded clip.
3. `stream` clips keep the whole AAC payload resident, decode ahead on the decode thread into a
   per-voice ring, and never decode on the mixer block. A long clip uses bounded memory; a
   short clip uses `memory`.
4. The mixer reconciles voices from the latest audio snapshot. Each source has a stable voice id;
   a source that disappears fades out, a new one starts, and changed properties ramp.
5. The mixer sums every voice plus master volume, applies a soft limiter, and queues stereo blocks.
6. An audio device change pauses the mixer, closes the device, opens the requested device, resets
   the output queue, and resumes. A failed open leaves the previous device active and reports the
   error. The sample rate is fixed at 48 kHz, so a device switch never changes the clip format.

Clip slots are bounded, decoded bytes are bounded, and a failure is a diagnostic plus silence, not an
out-of-memory crash. A catalog remount retires old clip slots after their voices stop; there is no
public unload request.

## Failure and mode semantics

| Situation | Behavior |
| --- | --- |
| Windowed startup (audio enabled) | Steam Audio context/HRTF and the SDL device are required. A failure is a startup `Bork2dException` naming the missing component, like a renderer startup failure. |
| Offscreen | Silent. `IAudio.IsAvailable == false`; no SDL audio, no Steam Audio, no mixer thread. Sources still update state. |
| Headless | Silent, same as offscreen. |
| `IEngine.Audio` before `Run` | Readable; sources can be created; device list is empty; one-shots return an invalid handle. |
| Retained references after `Run` | Playback and device members throw `ObjectDisposedException`; transform members still work. |
| `Dispose` | Idempotent; stops and joins audio before assets and the logger are disposed. |
| Missing clip payload | The voice is silent, one warning is logged on the control thread, and the game keeps running. |

## Tests and milestones

A1 - Bake and contract. `AudioClipAsset`, enums, sidecar, `AudioBaker`, FFmpeg resolution and version
gate, ADTS validation, encoder-delay calibration, diagnostics, Pong assets. Exit: a WAV and a FLAC
bake to ADTS with the expected frame count and delay, and repeat bakes on the same toolchain are
byte-identical; a missing encoder fails with `BK9404`.

A2 - Runtime foundation. `Bork2d.Audio` skeleton, SDL output seam, mixer thread, block queue, voice
pool, 2D playback, one-shots, AAC decode, memory mode, device enumeration. Exit: a known PCM clip
plays at the expected gain on a real device; the mixer allocates nothing after warm-up; offscreen and
headless are silent; the SDL lifecycle order is covered by fakes.

A3 - Spatial and scene audio. `IAudioSource`, scene capture, listener camera, flat-plane mapping,
Steam Audio HRTF, 2D/3D, pitch, looping, detach/reattach, stream mode, device switching, Pong
integration. Exit: a 3D source pans and elevates as the camera moves; a 2D source stays centered; a
detached source pauses; a device switch keeps playing; Pong sounds play; AOT publishes with natives.

