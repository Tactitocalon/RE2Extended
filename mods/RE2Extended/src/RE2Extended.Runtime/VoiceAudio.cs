using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace RE2Extended.Runtime
{
    // Minimal PCM WAV loader for the PoC. Supports 8-bit unsigned and 16-bit
    // signed PCM, mono or stereo. The supplied voice clips are 8-bit mono.
    internal static class WavClipLoader
    {
        private sealed class CacheEntry
        {
            internal long WriteTicks;
            internal AudioClip Clip;
        }

        private static readonly Dictionary<string, CacheEntry> Cache =
            new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);

        internal static AudioClip Load(string path, Action<string> log)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    if (log != null) log("Voice audio file not found: " + path);
                    return null;
                }

                long writeTicks = File.GetLastWriteTimeUtc(path).Ticks;
                CacheEntry cached;
                if (Cache.TryGetValue(path, out cached) &&
                    cached.Clip != null && cached.WriteTicks == writeTicks)
                    return cached.Clip;

                byte[] data = File.ReadAllBytes(path);
                AudioClip clip = Parse(path, data, log);
                if (clip == null) return null;

                Cache[path] = new CacheEntry
                {
                    WriteTicks = writeTicks,
                    Clip = clip
                };
                return clip;
            }
            catch (Exception error)
            {
                if (log != null) log("Voice audio load failed: " + error.GetType().Name + ": " + error.Message);
                return null;
            }
        }

        private static AudioClip Parse(string path, byte[] data, Action<string> log)
        {
            if (data == null || data.Length < 12 ||
                FourCC(data, 0) != "RIFF" || FourCC(data, 8) != "WAVE")
            {
                if (log != null) log("Voice audio is not a RIFF/WAVE file: " + path);
                return null;
            }

            int format = 0;
            int channels = 0;
            int sampleRate = 0;
            int bitsPerSample = 0;
            int dataOffset = -1;
            int dataLength = 0;

            int pos = 12;
            while (pos + 8 <= data.Length)
            {
                string id = FourCC(data, pos);
                int size = BitConverter.ToInt32(data, pos + 4);
                int body = pos + 8;
                if (size < 0 || body > data.Length) break;

                if (id == "fmt " && size >= 16)
                {
                    format = BitConverter.ToInt16(data, body);
                    channels = BitConverter.ToInt16(data, body + 2);
                    sampleRate = BitConverter.ToInt32(data, body + 4);
                    bitsPerSample = BitConverter.ToInt16(data, body + 14);
                }
                else if (id == "data")
                {
                    dataOffset = body;
                    dataLength = Math.Min(size, Math.Max(0, data.Length - body));
                }

                pos = body + size + (size & 1);
            }

            if (format != 1 || channels < 1 || channels > 2 ||
                (bitsPerSample != 8 && bitsPerSample != 16) ||
                sampleRate <= 0 || dataOffset < 0 || dataLength <= 0)
            {
                if (log != null)
                {
                    log("Voice audio format unsupported: format=" + format +
                        " channels=" + channels + " bits=" + bitsPerSample +
                        " rate=" + sampleRate + " data=" + dataLength);
                }
                return null;
            }

            int bytesPerSample = bitsPerSample / 8;
            int frameBytes = bytesPerSample * channels;
            int sampleCount = dataLength / frameBytes;
            if (sampleCount <= 0) return null;

            float[] samples = new float[sampleCount * channels];
            int outIndex = 0;
            for (int i = 0; i < sampleCount; i++)
            {
                int offset = dataOffset + i * frameBytes;
                for (int channel = 0; channel < channels; channel++)
                {
                    if (bitsPerSample == 8)
                    {
                        // 8-bit PCM WAV is unsigned, centered at 128.
                        samples[outIndex++] = (data[offset + channel] - 128) / 128f;
                    }
                    else
                    {
                        short value = BitConverter.ToInt16(data, offset + channel * 2);
                        samples[outIndex++] = value / 32768f;
                    }
                }
            }

            AudioClip clip = AudioClip.Create(
                "RE2ExtendedVoice", sampleCount, channels, sampleRate, false);
            if (clip == null) return null;
            clip.SetData(samples, 0);
            return clip;
        }

        private static string FourCC(byte[] data, int offset)
        {
            return Encoding.ASCII.GetString(data, offset, 4);
        }
    }

    internal sealed class VoiceEvent
    {
        internal readonly string Id;
        internal readonly string[] ClipPaths;

        internal VoiceEvent(string id, params string[] clipPaths)
        {
            Id = id;
            ClipPaths = clipPaths ?? new string[0];
        }
    }

    internal sealed class VoicePlayer : IDisposable
    {
        internal const int MaxQueueEvents = 4;
        private const float Volume = 0.8f;

        private readonly Queue<VoiceEvent> queue = new Queue<VoiceEvent>();
        private readonly Dictionary<string, AudioSource> alarmSources =
            new Dictionary<string, AudioSource>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> missingClipsLogged =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private AudioSource source;
        private VoiceQueueRunner runner;
        private VoiceEvent currentEvent;
        private List<AudioClip> currentClips;
        private int currentClipIndex;
        private bool sourceFailureLogged;
        private bool playbackFailureLogged;
        private bool alarmFailureLogged;

        internal void EnqueueEvent(VoiceEvent voiceEvent, Action<string> log)
        {
            try
            {
                if (voiceEvent == null) return;

                EnsureSource(log);
                if (source == null) return;

                // Do not repeat an event that is already playing or waiting.
                if (currentEvent != null &&
                    string.Equals(currentEvent.Id, voiceEvent.Id, StringComparison.OrdinalIgnoreCase))
                {
                    if (log != null)
                        log("Voice event already playing; skipping repeat: " +
                            voiceEvent.Id + ".");
                    return;
                }

                if (HasQueuedEvent(voiceEvent.Id))
                {
                    if (log != null)
                        log("Voice event already queued; skipping repeat: " +
                            voiceEvent.Id + ".");
                    return;
                }

                if (queue.Count >= MaxQueueEvents)
                {
                    if (log != null)
                        log("Voice event queue full (" + queue.Count + "/" +
                            MaxQueueEvents + "); dropping event: " + voiceEvent.Id + ".");
                    return;
                }

                queue.Enqueue(voiceEvent);
                if (log != null)
                    log("Voice queue: added event " + voiceEvent.Id + ", pending=" +
                        queue.Count + "/" + MaxQueueEvents + ".");
                Pump(log);
            }
            catch (Exception error)
            {
                if (log != null)
                    log("Voice queue event failed: " + error.GetType().Name + ": " + error.Message);
            }
        }

        private bool HasQueuedEvent(string eventId)
        {
            if (string.IsNullOrEmpty(eventId)) return false;

            foreach (VoiceEvent queued in queue)
            {
                if (string.Equals(queued.Id, eventId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        internal void PlayAlarm(string alarmId, string path, Action<string> log)
        {
            try
            {
                AudioSource alarmSource;
                if (alarmSources.TryGetValue(alarmId, out alarmSource) &&
                    alarmSource != null && alarmSource.isPlaying)
                {
                    if (log != null)
                        log("Alarm already playing; skipping repeat: " + alarmId + ".");
                    return;
                }

                AudioClip clip = WavClipLoader.Load(path, log);
                if (clip == null)
                {
                    if (missingClipsLogged.Add(path) && log != null)
                        log("Alarm clip unavailable; skipping it: " + path);
                    return;
                }

                if (alarmSource == null)
                    alarmSource = CreateAlarmSource(alarmId, log);
                if (alarmSource == null) return;

                alarmSource.clip = clip;
                alarmSource.volume = Volume;
                alarmSource.Play();
                if (log != null)
                    log("Alarm started: " + alarmId + ".");
            }
            catch (Exception error)
            {
                if (!alarmFailureLogged && log != null)
                {
                    alarmFailureLogged = true;
                    log("Alarm playback failed: " + error.GetType().Name + ": " + error.Message);
                }
            }
        }

        internal void Pump(Action<string> log)
        {
            try
            {
                if (source == null || source.isPlaying) return;

                // Continue the current event if it has more clips.
                if (currentEvent != null)
                {
                    if (currentClips != null && currentClipIndex < currentClips.Count)
                    {
                        PlayNextClip();
                        return;
                    }

                    currentEvent = null;
                    currentClips = null;
                    currentClipIndex = 0;
                }

                while (queue.Count > 0)
                {
                    VoiceEvent voiceEvent = queue.Dequeue();
                    List<AudioClip> clips = LoadClips(voiceEvent, log);
                    if (clips.Count == 0) continue;

                    currentEvent = voiceEvent;
                    currentClips = clips;
                    currentClipIndex = 0;
                    PlayNextClip();
                    return;
                }
            }
            catch (Exception error)
            {
                if (!playbackFailureLogged && log != null)
                {
                    playbackFailureLogged = true;
                    log("Voice playback failed: " + error.GetType().Name + ": " + error.Message);
                }
            }
        }

        private List<AudioClip> LoadClips(VoiceEvent voiceEvent, Action<string> log)
        {
            List<AudioClip> clips = new List<AudioClip>();
            foreach (string path in voiceEvent.ClipPaths)
            {
                if (string.IsNullOrEmpty(path)) continue;

                AudioClip clip = WavClipLoader.Load(path, log);
                if (clip == null)
                {
                    if (missingClipsLogged.Add(path) && log != null)
                        log("Voice clip unavailable; skipping it: " + path);
                    continue;
                }
                clips.Add(clip);
            }
            return clips;
        }

        private void PlayNextClip()
        {
            AudioClip clip = currentClips[currentClipIndex++];
            if (clip == null) return;

            source.clip = clip;
            source.volume = Volume;
            source.Play();
        }

        private void EnsureSource(Action<string> log)
        {
            if (source != null) return;

            try
            {
                GameObject gameObject = new GameObject("RE2Extended_VoiceInterface");
                UnityEngine.Object.DontDestroyOnLoad(gameObject);
                source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 0f; // 2D cockpit voice for the PoC.
                source.volume = 1f;

                runner = gameObject.AddComponent<VoiceQueueRunner>();
                runner.Owner = this;
            }
            catch (Exception error)
            {
                if (!sourceFailureLogged && log != null)
                {
                    sourceFailureLogged = true;
                    log("Voice audio source creation failed: " +
                        error.GetType().Name + ": " + error.Message);
                }
                source = null;
                runner = null;
            }
        }

        private AudioSource CreateAlarmSource(string alarmId, Action<string> log)
        {
            try
            {
                GameObject gameObject = new GameObject("RE2Extended_VoiceAlarm_" + alarmId);
                UnityEngine.Object.DontDestroyOnLoad(gameObject);
                AudioSource alarmSource = gameObject.AddComponent<AudioSource>();
                alarmSource.playOnAwake = false;
                alarmSource.loop = false;
                alarmSource.spatialBlend = 0f;
                alarmSource.volume = 1f;
                alarmSources[alarmId] = alarmSource;
                return alarmSource;
            }
            catch (Exception error)
            {
                if (!alarmFailureLogged && log != null)
                {
                    alarmFailureLogged = true;
                    log("Alarm audio source creation failed: " +
                        error.GetType().Name + ": " + error.Message);
                }
                return null;
            }
        }

        public void Dispose()
        {
            try
            {
                queue.Clear();
                if (source != null)
                    UnityEngine.Object.Destroy(source.gameObject);

                foreach (AudioSource alarmSource in alarmSources.Values)
                {
                    if (alarmSource != null)
                        UnityEngine.Object.Destroy(alarmSource.gameObject);
                }
                alarmSources.Clear();
            }
            catch
            {
                // Shutdown must not throw into the game.
            }
            finally
            {
                source = null;
                runner = null;
                currentEvent = null;
                currentClips = null;
                currentClipIndex = 0;
            }
        }
    }

    // Tiny main-thread pump. It does not scan the world; it only checks whether
    // the current voice clip has finished so the next queued clip can start.
    public sealed class VoiceQueueRunner : MonoBehaviour
    {
        internal VoicePlayer Owner;

        private void Update()
        {
            if (Owner != null)
                Owner.Pump(null);
        }
    }
}
