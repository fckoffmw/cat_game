using System.Collections.Generic;
using Unity.Netcode;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;

namespace Rooftop
{
    // Attach to Session. Counter names must identify byte totals PER FRAME, not cumulative totals.
    // NetworkConfig is configuration only; it does not expose traffic byte counters.
    public sealed class NetStats : MonoBehaviour
    {
        const double Interval = 3;
        const int Capacity = 4096;
        [SerializeField] string profilerCategory = "Network";
        [SerializeField] string receivedCounter = "Total Bytes Received";
        [SerializeField] string sentCounter = "Total Bytes Sent";

        readonly List<ProfilerRecorderSample> samples = new(Capacity);
        readonly List<ProfilerRecorderHandle> handles = new();
        ProfilerRecorder incoming, outgoing;
        NetworkManager session;
        double startedAt, nextAttempt;
        long receivedBytes, sentBytes;
        bool warned;

        void LateUpdate()
        {
            var manager = NetworkManager.Singleton;
            if (!manager || !manager.IsListening)
            {
                ResetSession();
                return;
            }
            if (session != manager)
            {
                ResetSession();
                session = manager;
            }

            double now = Time.realtimeSinceStartupAsDouble;
            if (!incoming.Valid || !outgoing.Valid)
            {
                if (now < nextAttempt) return;
                nextAttempt = now + Interval;
                if (!TryStart()) return;
                startedAt = now;
                receivedBytes = sentBytes = 0;
            }

            // Drain every frame; summing just LastValue once per three seconds loses traffic.
            bool overflow = incoming.WrappedAround || outgoing.WrappedAround;
            receivedBytes += Drain(incoming);
            sentBytes += Drain(outgoing);
            if (overflow)
            {
                Debug.LogWarning("NetStats: буфер ProfilerRecorder переполнен; интервал замера пропущен.");
                startedAt = now;
                receivedBytes = sentBytes = 0;
                return;
            }
            double elapsed = now - startedAt;
            if (elapsed < Interval) return;
            // Decimal KB, consistent with Tools/udp_lab.py and the existing PR-5 report.
            Debug.Log($"Входящий трафик: {receivedBytes / elapsed / 1000:F2} KB/s, Исходящий: {sentBytes / elapsed / 1000:F2} KB/s");
            startedAt = now;
            receivedBytes = sentBytes = 0;
        }

        bool TryStart()
        {
            incoming.Dispose();
            outgoing.Dispose();
            var category = new ProfilerCategory(profilerCategory);
            if (!IsByteCounter(category, receivedCounter) || !IsByteCounter(category, sentCounter))
            {
                WarnUnavailable();
                return false;
            }
            var options = ProfilerRecorderOptions.StartImmediately |
                ProfilerRecorderOptions.WrapAroundWhenCapacityReached |
                ProfilerRecorderOptions.SumAllSamplesInFrame;
            incoming = ProfilerRecorder.StartNew(category, receivedCounter, Capacity, options);
            outgoing = ProfilerRecorder.StartNew(category, sentCounter, Capacity, options);
            if (incoming.Valid && outgoing.Valid) return true;
            WarnUnavailable();
            return false;
        }

        bool IsByteCounter(ProfilerCategory category, string name)
        {
            handles.Clear();
            ProfilerRecorderHandle.GetAvailable(handles);
            foreach (var handle in handles)
            {
                var description = ProfilerRecorderHandle.GetDescription(handle);
                if (description.Category.Name == category.Name && description.Name == name &&
                    description.UnitType == ProfilerMarkerDataUnit.Bytes) return true;
            }
            return false;
        }

        long Drain(ProfilerRecorder recorder)
        {
            samples.Clear();
            recorder.CopyTo(samples, true);
            long total = 0;
            foreach (var sample in samples) total += sample.Value;
            return total;
        }

        void WarnUnavailable()
        {
            if (warned) return;
            warned = true;
            Debug.LogWarning($"NetStats: счётчики байтов '{profilerCategory}/{receivedCounter}' и " +
                $"'{profilerCategory}/{sentCounter}' недоступны. NetworkConfig не содержит байтов. " +
                "В текущем NGO без Multiplayer Tools используется NullNetworkMetrics. " +
                "Укажите реальные счётчики байтов за кадр в Inspector или используйте Tools/udp_lab.py; KB/s не измерены.");
        }

        void ResetSession()
        {
            incoming.Dispose();
            outgoing.Dispose();
            incoming = outgoing = default;
            session = null;
            receivedBytes = sentBytes = 0;
            nextAttempt = 0;
            warned = false;
        }

        void OnDisable() => ResetSession();
    }
}
