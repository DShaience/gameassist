using System;
using System.IO;
using System.Media;
using System.Threading.Tasks;
using GameScreenTranslator.Core.Configuration;
using GameScreenTranslator.Core.Logging;
using NAudio.Wave;

namespace GameScreenTranslator.Core.Audio;

/// <summary>
/// Plays short non-speech audio cues (earcons) to indicate state changes.
/// Supports both synthetic tones and MP3/WAV file playback.
/// </summary>
public sealed class EarconPlayer
{
    private readonly AppSettings.AudioSettings.EarconsSettings _settings;
    private readonly string _baseDirectory;

    public EarconPlayer(AppSettings.AudioSettings.EarconsSettings settings, string baseDirectory)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _baseDirectory = baseDirectory ?? throw new ArgumentNullException(nameof(baseDirectory));
    }

    /// <summary>
    /// Plays the listening chime when STT recording begins (tap-to-talk).
    /// Fire-and-forget; returns immediately.
    /// </summary>
    public void PlayListeningChime()
    {
        if (!_settings.Enabled || !_settings.ListeningChimeEnabled)
            return;

        var path = ResolvePath(_settings.ListeningChimePath);
        if (string.IsNullOrWhiteSpace(path))
            return;

        PlayAudioFileAsync(path, _settings.Volume);
    }

    /// <summary>
    /// Plays the thinking chime after STT ends while waiting for the model response.
    /// </summary>
    public void PlayThinkingChime()
    {
        if (!_settings.Enabled || !_settings.ThinkingChimeEnabled)
            return;

        // Synthetic dual-tone chime (existing behavior)
        PlayToneSequence(
            frequencyHz1: 880,
            frequencyHz2: 1320,
            toneMs: 90,
            gapMs: 40,
            volume: _settings.Volume);
    }

    /// <summary>
    /// Plays the cancel chime when user presses stop (optional, off by default).
    /// </summary>
    public void PlayCancelChime()
    {
        if (!_settings.Enabled || !_settings.CancelChimeEnabled)
            return;

        PlayToneSequence(
            frequencyHz1: 220,
            frequencyHz2: 165,
            toneMs: 80,
            gapMs: 30,
            volume: Math.Min(_settings.Volume, 0.10));
    }

    private string? ResolvePath(string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
            return null;

        if (Path.IsPathRooted(configuredPath))
            return configuredPath;

        return Path.Combine(_baseDirectory, configuredPath);
    }

    private static void PlayAudioFileAsync(string path, double volume)
    {
        _ = Task.Run(() =>
        {
            try
            {
                if (!File.Exists(path))
                {
                    Log.Warn($"Earcon file not found: {path}");
                    return;
                }

                var extension = Path.GetExtension(path).ToLowerInvariant();

                if (extension == ".mp3")
                {
                    PlayMp3(path, volume);
                }
                else if (extension == ".wav")
                {
                    PlayWav(path, volume);
                }
                else
                {
                    Log.Warn($"Unsupported earcon format: {extension}");
                }
            }
            catch (Exception ex)
            {
                // Earcons are best-effort; don't crash the app
                Log.Warn($"Failed to play earcon: {ex.Message}");
            }
        });
    }

    private static void PlayMp3(string path, double volume)
    {
        using var audioFile = new Mp3FileReader(path);
        using var volumeStream = new WaveChannel32(audioFile)
        {
            Volume = (float)Math.Clamp(volume, 0.0, 1.0)
        };
        using var outputDevice = new WaveOutEvent();

        outputDevice.Init(volumeStream);
        outputDevice.Play();

        // Wait for playback to complete
        while (outputDevice.PlaybackState == PlaybackState.Playing)
        {
            System.Threading.Thread.Sleep(50);
        }
    }

    private static void PlayWav(string path, double volume)
    {
        // For WAV, use NAudio for consistent volume control
        using var audioFile = new WaveFileReader(path);
        using var volumeStream = new WaveChannel32(audioFile)
        {
            Volume = (float)Math.Clamp(volume, 0.0, 1.0)
        };
        using var outputDevice = new WaveOutEvent();

        outputDevice.Init(volumeStream);
        outputDevice.Play();

        while (outputDevice.PlaybackState == PlaybackState.Playing)
        {
            System.Threading.Thread.Sleep(50);
        }
    }

    private static void PlayToneSequence(int frequencyHz1, int frequencyHz2, int toneMs, int gapMs, double volume)
    {
        // Fire-and-forget; keep it extremely short to avoid UX annoyance.
        _ = Task.Run(() =>
        {
            try
            {
                var wav = WavTone.CreateTwoToneChime(frequencyHz1, frequencyHz2, toneMs, gapMs, volume);
                using var ms = new MemoryStream(wav);
                using var player = new SoundPlayer(ms);
                player.Load();
                player.PlaySync();
            }
            catch
            {
                // Earcons are best-effort.
            }
        });
    }

    private static class WavTone
    {
        // Minimal PCM WAV generator (16-bit, mono).
        public static byte[] CreateTwoToneChime(int hz1, int hz2, int toneMs, int gapMs, double volume)
        {
            const int sampleRate = 44100;
            const short channels = 1;
            const short bitsPerSample = 16;

            volume = Clamp(volume, 0.0, 1.0);
            // Apply a conservative cap so it stays "soft" even if system volume is high.
            var amplitude = (short)(Clamp(volume, 0.0, 1.0) * 0.25 * short.MaxValue);

            var samples1 = (int)(sampleRate * (toneMs / 1000.0));
            var gapSamples = (int)(sampleRate * (gapMs / 1000.0));
            var samples2 = (int)(sampleRate * (toneMs / 1000.0));

            var totalSamples = samples1 + gapSamples + samples2;
            var dataBytes = totalSamples * (bitsPerSample / 8);

            using var ms = new MemoryStream(44 + dataBytes);
            using var bw = new BinaryWriter(ms);

            // RIFF header
            bw.Write("RIFF"u8.ToArray());
            bw.Write(36 + dataBytes);
            bw.Write("WAVE"u8.ToArray());

            // fmt chunk
            bw.Write("fmt "u8.ToArray());
            bw.Write(16);
            bw.Write((short)1); // PCM
            bw.Write(channels);
            bw.Write(sampleRate);
            var byteRate = sampleRate * channels * (bitsPerSample / 8);
            bw.Write(byteRate);
            var blockAlign = (short)(channels * (bitsPerSample / 8));
            bw.Write(blockAlign);
            bw.Write(bitsPerSample);

            // data chunk
            bw.Write("data"u8.ToArray());
            bw.Write(dataBytes);

            WriteTone(bw, sampleRate, hz1, samples1, amplitude);
            WriteSilence(bw, gapSamples);
            WriteTone(bw, sampleRate, hz2, samples2, amplitude);

            bw.Flush();
            return ms.ToArray();
        }

        private static void WriteSilence(BinaryWriter bw, int samples)
        {
            for (var i = 0; i < samples; i++)
                bw.Write((short)0);
        }

        private static void WriteTone(BinaryWriter bw, int sampleRate, int hz, int samples, short amplitude)
        {
            // Envelope (avoid clicks): quick attack + release.
            var attackSamples = Math.Max(1, (int)(sampleRate * 0.008));
            var releaseSamples = Math.Max(1, (int)(sampleRate * 0.020));

            for (var i = 0; i < samples; i++)
            {
                var t = i / (double)sampleRate;
                var raw = Math.Sin(2 * Math.PI * hz * t);

                double env;
                if (i < attackSamples)
                    env = i / (double)attackSamples;
                else if (i > samples - releaseSamples)
                    env = Math.Max(0.0, (samples - i) / (double)releaseSamples);
                else
                    env = 1.0;

                var sample = (short)(raw * amplitude * env);
                bw.Write(sample);
            }
        }

        private static double Clamp(double v, double min, double max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }
    }
}
