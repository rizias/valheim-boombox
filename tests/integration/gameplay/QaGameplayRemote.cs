using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using VikingBoombox;

public sealed partial class QaGameplay
{
    private static QaGameplay remoteObserver;
    private readonly JArray remoteSent = new JArray();
    private readonly JArray remoteShown = new JArray();
    private int remoteDisposedCalls;
    private bool RemoteOperation(string operation, JObject request)
    {
        if (!operation.StartsWith("remote-", StringComparison.Ordinal))
            return false;
        if (operation == "remote-watch")
        {
            remoteObserver = this;
            safetyHarmony.Patch(
                AccessTools.Method(typeof(AudioDeck), "OnBeat"),
                prefix: new HarmonyMethod(typeof(QaGameplay), nameof(RemoteSourceBeat))
            );
            safetyHarmony.Patch(
                AccessTools.Method(typeof(BoomboxRemoteView), "LateUpdate"),
                prefix: new HarmonyMethod(typeof(QaGameplay), nameof(RemotePresentBefore)),
                postfix: new HarmonyMethod(typeof(QaGameplay), nameof(RemotePresentAfter))
            );
            return true;
        }
        if (operation == "remote-snapshot")
        {
            sample = RemoteSnapshot();
            return true;
        }
        var plugin = BoomboxPlugin.Instance;
        if (operation == "remote-adopt")
        {
            var registry = (Dictionary<string, AudioDeck>)AccessTools.Field(typeof(BoomboxPlugin), "Decks")
                .GetValue(plugin);
            var adopted = registry.Values.Single(d => d != null && d.State.id == (string)request["source"]);
            decks["A"] = adopted;
            if (!adopted.State.playing)
                adopted.TogglePlayback();
            return true;
        }
        if (operation == "remote-setup")
        {
            plugin.Settings.ShowVisualEffects.Value =
                plugin.Settings.ShowSpeakerEffects.Value =
                plugin.Settings.ShowBodyGlow.Value =
                plugin.Settings.ShowSigurdGlow.Value =
                true;
            plugin.Settings.ShowOthersWaves.Value = true;
            plugin.Settings.DecorationMovement.Value = plugin.Settings.DecorationMusicMovement.Value = true;
            plugin.Settings.DecorationParticles.Value = false;
            plugin.Settings.Effect.CreatureDamage.Value = false;
            return true;
        }
        if (operation == "remote-toggle")
        {
            plugin.Settings.ShowOthersWaves.Value = (bool)request["visible"];
            return true;
        }
        if (operation == "remote-dispose")
        {
            var view = BoomboxRemoteNetwork.Current.Views.Single(v => v != null
                && v.SourceId == (string)request["source"]);
            var dispose = AccessTools.Method(typeof(BoomboxRemoteView), "Dispose");
            dispose.Invoke(view, null);
            dispose.Invoke(view, null);
            remoteDisposedCalls += 2;
            return true;
        }
        if (operation == "remote-mutate")
        {
            var deck = decks[(string)request["key"]];
            deck.State.insertedCassette.properties["Red"] = (int)request["rank"];
            if (!deck.State.unlockedDecorationPrefabs.Contains("TrophySkeleton"))
                deck.State.unlockedDecorationPrefabs.Add("TrophySkeleton");
            deck.State.decorationPlacements.Clear();
            deck.State.decorationPlacements.Add(
                new BoomboxDecorationPlacement
                {
                    slot = 17,
                    prefab = "TrophySkeleton",
                    effect = "Damage",
                    effectParticles = false
                }
            );
            deck.State.decorationPlacements.Add(
                new BoomboxDecorationPlacement
                {
                    slot = 0,
                    prefab = "MushroomYellow",
                    effect = "Stamina",
                    effectParticles = false
                }
            );
            deck.VisualPresenter.RefreshState(true);
            plugin.Save();
            sample = new JObject
            {
                ["unlockedPrefabs"] = new JArray(deck.State.unlockedDecorationPrefabs),
                ["unlockedSlots"] = new JArray(deck.State.unlockedDecorationSlots)
            };
            return true;
        }
        if (operation == "remote-pickup")
        {
            if (!plugin.Items.PickUp(decks[(string)request["key"]]))
                throw new InvalidOperationException("Native product pickup rejected owned fixture");
            return true;
        }
        throw new InvalidOperationException("Unknown focused remote operation");
    }
    private static void RemoteSourceBeat(AudioDeck __instance, float strength, double timestamp)
    {
        if (remoteObserver == null)
            return;
        if (remoteObserver.remoteSent.Count >= 128)
            remoteObserver.remoteSent.RemoveAt(0);
        remoteObserver.remoteSent.Add(new JObject
        {
            ["source"] = __instance.State.id,
            ["time"] = timestamp,
            ["strength"] = strength,
            ["utcTicks"] = DateTime.UtcNow.Ticks
        });
    }
    private static void RemotePresentBefore(BoomboxRemoteView __instance, out JObject __state)
    {
        __state = null;
        if (remoteObserver == null || __instance == null)
            return;
        var fresh = (List<MusicStrike>)AccessTools.Field(typeof(BoomboxRemoteView), "fresh").GetValue(__instance);
        __state = new JObject
        {
            ["source"] = __instance.SourceId,
            ["before"] = __instance.PresentedEvents,
            ["fresh"] = new JArray(fresh.Select(s => s.Time))
        };
    }
    private static void RemotePresentAfter(BoomboxRemoteView __instance, JObject __state)
    {
        if (remoteObserver == null
            || __state == null
            || __instance == null
            || __instance.PresentedEvents <= (int)__state["before"])
            return;
        foreach (var time in (JArray)__state["fresh"])
        {
            if (remoteObserver.remoteShown.Count >= 128)
                remoteObserver.remoteShown.RemoveAt(0);
            remoteObserver.remoteShown.Add(
                new JObject { ["source"] = __state["source"], ["time"] = time, ["utcTicks"] = DateTime.UtcNow.Ticks }
            );
        }
    }
    private JObject RemoteSnapshot()
    {
        var service = BoomboxRemoteNetwork.Current;
        var views = service == null ? Array.Empty<BoomboxRemoteView>() : service.Views.Where(v => v != null).ToArray();
        var aggregate = BoomboxAuraNetwork.For(Player.m_localPlayer);
        return new JObject
        {
            ["utcTicks"] = DateTime.UtcNow.Ticks,
            ["showOthers"] = BoomboxPlugin.Instance.Settings.ShowOthersWaves.Value,
            ["disposedCalls"] = remoteDisposedCalls,
            ["sent"] = remoteSent.DeepClone(),
            ["shown"] = remoteShown.DeepClone(),
            ["values"] = new JArray(aggregate.Values),
            ["ownedModels"] = new JArray(
                decks.Values.Where(d => d != null && d.Visual != null).Select(
                    d => new JObject { ["source"] = d.State.id, ["position"] = Point(d.Visual.transform.position) }
                )
            ),
            ["views"] = new JArray(
            views.Select(
            v =>
                    {
                        var state = (BoomboxRemoteState)AccessTools.Field(typeof(BoomboxRemoteView), "state")
                            .GetValue(v);
                        var effects = AccessTools.Field(typeof(BoomboxRemoteView), "effects").GetValue(v);
                        var effectView = effects == null
                            ? null
                            : AccessTools.Field(effects.GetType(), "view").GetValue(effects);
                        return new JObject
                        {
                            ["source"] = v.SourceId,
                            ["owner"] = v.SourceOwner,
                            ["peer"] = state.Peer,
                            ["character"] = state.Character.ToString(),
                            ["instance"] = v.GetInstanceID(),
                            ["model"] = v.Model != null,
                            ["active"] = v.gameObject.activeInHierarchy,
                            ["position"] = Point(v.transform.position),
                            ["modelPosition"] = v.Model != null ? Point(v.Model.transform.position) : null,
                            ["parent"] = v.transform.parent?.name,
                            ["playing"] = v.Playing,
                            ["events"] = v.PresentedEvents,
                            ["revision"] = v.PresentedRevision,
                            ["waveView"] = effectView != null,
                            ["motion"] = state.Motion,
                            ["glow"] = state.Glow,
                            ["redRank"] = state.Appearance.insertedCassette?.Rank("Red") ?? 0,
                            ["decorations"] = new JArray(
                        state.Appearance.decorationPlacements.Select(
                            p => new JObject { ["slot"] = p.slot, ["prefab"] = p.prefab }
                        )
                    ),
                            ["audioSources"] = v.GetComponentsInChildren<AudioSource>(true).Length,
                            ["materials"] = v.GetComponentsInChildren<Renderer>(true)
                                .SelectMany(r => r.sharedMaterials)
                                .Where(
                        m => m != null
                    )
                                .Select(
                        m => m.GetInstanceID()
                    )
                                .Distinct()
                                .Count()
                        };
                    }
            )
            ),
            ["liveViewObjects"] = UnityEngine.Object.FindObjectsByType<BoomboxRemoteView>(FindObjectsSortMode.None)
                .Length
        };
    }
}
