#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Unity.Netcode;
using UnityEngine;

namespace Rooftop
{
    // The PR-5 probe can run without Multiplayer Tools. When installed, consume the
    // same per-object NGO events as its Network Profiler through the package's internal API.
    internal sealed class MatchTrafficMetrics
    {
        const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        readonly EventInfo publisher;
        readonly Delegate handler;
        readonly Dictionary<string, (long messages, long bytes)> totals = new();
        readonly Dictionary<Type, PropertyInfo> eventValues = new();
        PropertyInfo metrics;

        MatchTrafficMetrics(EventInfo publisher)
        {
            this.publisher = publisher;
            handler = Delegate.CreateDelegate(publisher.EventHandlerType, this, nameof(Observe));
            publisher.GetAddMethod(true).Invoke(null, new object[] { handler });
        }

        internal static MatchTrafficMetrics TryAttach()
        {
            const string publisherType = "Unity.Multiplayer.Tools.MetricEvents.MetricEventPublisher";
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var publisher = assembly.GetType(publisherType)?.GetEvent("OnMetricsReceived", StaticMembers);
                if (publisher != null) return new MatchTrafficMetrics(publisher);
            }
            Debug.Log("MATCH_METRICS_UNAVAILABLE (Multiplayer Tools required for per-object events)");
            return null;
        }

        void Observe(object collection)
        {
            metrics ??= collection.GetType().GetProperty("Metrics");
            foreach (var metric in (IEnumerable)metrics.GetValue(collection))
            {
                var type = metric.GetType();
                if (!type.IsGenericType) continue;
                var payload = type.GetGenericArguments()[0];
                if (payload.Name is not ("RpcEvent" or "NetworkVariableEvent")) continue;
                if (!((string)type.GetProperty("Name").GetValue(metric)).EndsWith("Sent", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!eventValues.TryGetValue(type, out var values))
                {
                    values = type.GetProperty("Values");
                    eventValues.Add(type, values);
                }
                var connection = payload.GetProperty("Connection");
                var behavior = payload.GetProperty("NetworkBehaviourName");
                var name = payload.GetProperty("Name");
                var bytes = payload.GetProperty("BytesCount");
                foreach (var item in (IEnumerable)values.GetValue(metric))
                {
                    var peer = connection.GetValue(item);
                    if ((ulong)peer.GetType().GetProperty("Id").GetValue(peer) == NetworkManager.ServerClientId)
                        continue;
                    string key = behavior.GetValue(item) + "." + name.GetValue(item);
                    totals.TryGetValue(key, out var total);
                    totals[key] = (total.messages + 1, total.bytes + (long)bytes.GetValue(item));
                }
            }
        }

        internal void Finish()
        {
            publisher.GetRemoveMethod(true).Invoke(null, new object[] { handler });
            foreach (var entry in totals)
                Debug.Log($"MATCH_METRIC {entry.Key} messages={entry.Value.messages} bytes={entry.Value.bytes}");
        }
    }
}
#endif
