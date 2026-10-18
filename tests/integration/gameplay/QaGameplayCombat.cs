using System;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using VikingBoombox;

public sealed partial class QaGameplay
{
    private ZDOID npcId;
    private JObject lastStrike;
    private JObject lastReplay;
    private static Vector3? combatArea;
    private Harmony combatAreaHarmony;

    private void InstallCombatArea()
    {
        combatArea = null;
        combatAreaHarmony = new Harmony("local.boombox.qa.combat.area");
        combatAreaHarmony.Patch(
            AccessTools.Method(typeof(ZNet), "SetReferencePosition"),
            prefix: new HarmonyMethod(typeof(QaGameplay), nameof(SelectCombatArea))
        );
    }
    private static void SelectCombatArea(ref Vector3 __0)
    {
        if (Environment.GetEnvironmentVariable("BOOMBOX_QA_ROLE") != "server"
            || !combatArea.HasValue
            || __0.x != 1000000f
            || __0.y != 0f
            || __0.z != 1000000f)
            return;
        __0 = combatArea.Value;
    }
    private void StopCombatArea()
    {
        combatArea = null;
        combatAreaHarmony?.UnpatchSelf();
    }

    private bool Combat(string operation, JObject request)
    {
        if (!operation.StartsWith("combat-", StringComparison.Ordinal))
            return false;
        if (DamageRaceOperation(operation, request) || DamagePhysicsOperation(operation, request))
            return true;
        var settings = BoomboxPlugin.Instance.Settings;
        if (operation == "combat-area")
        {
            if (!ZNet.instance.IsServer())
                throw new InvalidOperationException("Native scene area fixture requires D");
            combatArea = ReadPoint(request["position"]);
            ZNet.instance.SetReferencePosition(combatArea.Value);
            return true;
        }
        if (operation == "combat-host")
        {
            Game.instance.SetForcePlayerDifficulty(1);
            settings.Damage[1].Value = 10f;
            settings.Push[1].Value = 0f;
            settings.Radius[1].Value = 20f;
            settings.Effect.CreatureDamage.Value = true;
            return true;
        }
        if (operation == "combat-mute")
        {
            Game.instance.SetForcePlayerDifficulty(1);
            settings.Effect.CreatureDamage.Value = false;
            return true;
        }
        if (operation == "combat-clean-old")
        {
            if (!ZNet.instance.IsServer())
                throw new InvalidOperationException("Owned QA NPC cleanup requires D");
            foreach (var character in Character.GetAllCharacters().ToArray())
            {
                var view = character.GetComponent<ZNetView>();
                var zdo = view?.GetZDO();
                if (zdo == null || !zdo.GetBool("boombox.qa.npc", false))
                    continue;
                zdo.SetOwner(ZNet.GetUID());
                if (!view.IsOwner())
                    throw new InvalidOperationException("QA NPC cleanup ownership not established");
                ZNetScene.instance.Destroy(character.gameObject);
            }
            return true;
        }
        if (operation == "combat-spawn")
        {
            if (!ZNet.instance.IsServer())
                throw new InvalidOperationException("Native NPC creation requires D");
            string prefab = (string)request["prefab"] ?? "Skeleton";
            if (prefab != "Skeleton" && prefab != "Deer")
                throw new InvalidOperationException("Owned QA NPC prefab outside focused cases");
            var root = UnityEngine.Object.Instantiate(
                ZNetScene.instance.GetPrefab(prefab),
                ReadPoint(request["position"]),
                Quaternion.identity
            );
            npcId = root.GetComponent<ZNetView>().GetZDO().m_uid;
            root.GetComponent<ZNetView>().GetZDO().Set("boombox.qa.npc", true);
            ControlNpc((bool?)request["dynamic"] ?? false);
            return true;
        }
        if (request["uid"] != null)
            npcId = new ZDOID((long)request["uid"], (uint)request["object"]);
        if (operation == "combat-owner" || operation == "combat-owner-local")
        {
            var zdo = ZDOMan.instance.GetZDO(npcId);
            if (zdo == null || !zdo.GetBool("boombox.qa.npc", false))
                throw new InvalidOperationException("Owned QA NPC missing");
            if (!ZNet.instance.IsServer() && (operation != "combat-owner-local" || zdo.GetOwner() != ZNet.GetUID()))
                throw new InvalidOperationException("NPC ownership fixture requires actual current native owner");
            zdo.SetOwner((long)request["peer"]);
            return true;
        }
        if (operation == "combat-control")
        {
            ControlNpc();
            return true;
        }
        if (operation == "combat-create-strike")
        {
            Create(
                (string)request["key"],
                ((JArray)request["ranks"]).Select(t => (int)t).ToArray(),
                0,
                true,
                ReadPoint(request["position"])
            );
            return Combat("combat-strike", request);
        }
        if (operation == "combat-strike")
        {
            var deck = decks[(string)request["key"]];
            var moment = new MusicMoment
            {
                Time = deck.Moment.Time,
                Level = 1f,
                Silent = false,
                Smooth = new MusicRanges { Whole = 1, Low = 1, Mid = 1, High = 1 }
            };
            moment.Fresh.Add(new MusicStrike { Time = moment.Time, Strength = 1f, Loudest = MusicRange.Low });
            settings.Effect.Variant.Value = BoomboxMusicEffect.Sparks;
            settings.Effect.CreatureDamage.Value = true;
            try
            {
                deck.Effect.Step(moment, true);
                moment.Fresh.Clear();
                moment.Time += .16;
                deck.Effect.Step(moment, true);
            }
            finally
            {
                settings.Effect.CreatureDamage.Value = false;
            }
            var service = BoomboxMusicNetwork.Current;
            lastStrike = new JObject
            {
                ["source"] = deck.State.id,
                ["epoch"] = AccessTools.Field(typeof(BoomboxMusicNetwork), "epoch").GetValue(service).ToString(),
                ["sequence"] = (long)AccessTools.Field(typeof(BoomboxMusicNetwork), "sequence").GetValue(service),
                ["target"] = npcId.ToString()
            };
            return true;
        }
        if (operation == "combat-replay")
        {
            var source = request["event"] ?? lastStrike;
            var begin = new ZPackage();
            begin.Write((string)source["source"]);
            begin.Write((string)source["epoch"]);
            begin.Write((long)source["sequence"]);
            var hit = new ZPackage();
            hit.Write((string)source["source"]);
            hit.Write((string)source["epoch"]);
            hit.Write((long)source["sequence"]);
            hit.Write(npcId);
            hit.Write(1f);
            var parser = typeof(BoomboxPlugin).Assembly.GetType("VikingBoombox.BoomboxMusicHit", true);
            var parsed = AccessTools.Method(parser, "Read")
                .Invoke(null, new object[] { new ZPackage(hit.GetArray()), false, false });
            if (!((ZDOID)AccessTools.Field(parser, "Target").GetValue(parsed)).Equals(npcId)
            || (long)AccessTools.Field(parser, "Sequence").GetValue(parsed) != (long)source["sequence"])
                throw new InvalidOperationException("Replay packet did not round-trip");
            using (var hash = System.Security.Cryptography.SHA256.Create())
                lastReplay = new JObject
                {
                    ["valid"] = true,
                    ["sha"] = BitConverter.ToString(hash.ComputeHash(hit.GetArray())).Replace("-", ""),
                    ["event"] = source.DeepClone()
                };
            var server = ZNet.instance.GetPeers().Single(p => p.m_server && p.IsReady());
            server.m_rpc.Invoke("BoomboxMusicHit", hit);
            server.m_rpc.Invoke("BoomboxMusicBegin", begin);
            server.m_rpc.Invoke("BoomboxMusicHit", hit);
            return true;
        }
        if (operation == "combat-clear")
        {
            var root = ZNetScene.instance.FindInstance(npcId);
            if (root != null)
            {
                var view = root.GetComponent<ZNetView>();
                if (!view.IsOwner())
                    throw new InvalidOperationException("QA NPC cleanup requires verified owner");
                ZNetScene.instance.Destroy(root);
            }
            npcId = ZDOID.None;
            return true;
        }
        if (operation == "combat-snapshot")
            return true;
        throw new InvalidOperationException("Unknown combat fixture action");
    }

    private void ControlNpc(bool dynamicBody = false)
    {
        var character = ZNetScene.instance.FindInstance(npcId)?.GetComponent<Character>();
        if (character == null || !character.GetComponent<ZNetView>().GetZDO().GetBool("boombox.qa.npc", false))
            throw new InvalidOperationException("Native QA NPC not instantiated");
        character.GetBaseAI().enabled = false;
        AccessTools.Field(typeof(Character), "m_damageModifiers").SetValue(character, new HitData.DamageModifiers());
        var body = character.GetComponent<Rigidbody>();
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.constraints = dynamicBody ? RigidbodyConstraints.FreezeRotation : RigidbodyConstraints.FreezeAll;
        if (character.IsOwner())
        {
            character.SetMaxHealth(1000f);
            character.SetHealth(1000f);
        }
    }

    private JObject CombatSnapshot()
    {
        if (Game.instance == null || ZNet.instance == null || ZNetScene.instance == null)
            return new JObject { ["ready"] = false };
        var character = npcId.IsNone() ? null : ZNetScene.instance.FindInstance(npcId)?.GetComponent<Character>();
        var service = BoomboxMusicNetwork.Current;
        var difficulty = Game.instance.GetDifficultyDamageScaleEnemy(character != null
            ? character.transform.position
            : ZNet.instance.GetReferencePosition());
        if (float.IsNaN(difficulty) || float.IsInfinity(difficulty))
            throw new InvalidOperationException("Native difficulty fixture invalid");
        return new JObject
        {
            ["reference"] = Point(ZNet.instance.GetReferencePosition()),
            ["areaReady"] = ZNetScene.instance != null
                && ZNetScene.instance.IsAreaReady(ZNet.instance.GetReferencePosition()),
            ["areaLoaded"] = ZoneSystem.instance != null && ZoneSystem.instance.IsActiveAreaLoaded(),
            ["nativeDifficultyDamageScale"] = difficulty,
            ["replay"] = lastReplay,
            ["npc"] = character == null
                ? null
                : new JObject
                {
                    ["id"] = npcId.ToString(),
                    ["health"] = character.GetHealth(),
                    ["owner"] = character.GetOwner(),
                    ["isOwner"] = character.IsOwner(),
                    ["position"] = Point(character.transform.position),
                    ["faction"] = character.GetFaction().ToString(),
                    ["isPlayer"] = character.IsPlayer(),
                    ["tamed"] = character.IsTamed()
                },
            ["sourcePlayers"] = new JArray(
                Player.GetAllPlayers().Select(
                    p => new JObject
                    {
                        ["id"] = p.GetPlayerID(),
                        ["zdo"] = p.GetZDOID().ToString(),
                        ["local"] = p == Player.m_localPlayer
                    }
                )
            ),
            ["begun"] = service?.BegunEvents,
            ["routed"] = service?.RoutedHits,
            ["applied"] = service?.AppliedHits,
            ["dropped"] = service?.DroppedHits,
            ["lastDamage"] = service?.LastDamage,
            ["event"] = lastStrike
        };
    }
}
