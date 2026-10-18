using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

public sealed partial class QaGameplay
{
    private bool auraMoving;
    private float auraMoveStart;
    private readonly List<float> auraSpeeds = new List<float>();
    private readonly List<float> auraTargets = new List<float>();
    private readonly List<float> auraBases = new List<float>();
    private readonly List<float> auraTimes = new List<float>();
    private readonly JArray auraUnexpectedContacts = new JArray();
    private readonly JArray auraFreshness = new JArray();
    private float auraNativeBase, auraNativeTarget;
    private float auraNativeTick;
    private int auraNativeCalls;
    private static QaGameplay auraController;
    private PhysicsMaterial auraFloorMaterial;
    private bool AuraMotionOperation(string operation, JObject request)
    {
        if (operation == "aura-position")
        {
            var player = Player.m_localPlayer;
            if (player == null || player.IsTeleporting() || auraMoving)
                throw new InvalidOperationException("Numeric fixture position cannot change during movement");
            var body = player.GetComponent<Rigidbody>();
            body.position = player.transform.position = ReadPoint(request["position"]);
            body.linearVelocity = body.angularVelocity = Vector3.zero;
            Physics.SyncTransforms();
            return true;
        }
        if (operation == "aura-floor")
        {
            var floor = new GameObject("Boombox QA native movement runway");
            floor.transform.position = ReadPoint(request["position"]) + Vector3.down;
            floor.layer = LayerMask.NameToLayer("Default");
            var collider = floor.AddComponent<BoxCollider>();
            collider.size = new Vector3(100f, 1f, 100f);
            auraFloorMaterial = new PhysicsMaterial(
                "QA neutral ground friction"
            )
            {
                staticFriction = 0f,
                dynamicFriction = 0f,
                frictionCombine = PhysicsMaterialCombine.Multiply
            };
            collider.sharedMaterial = auraFloorMaterial;
            fixtures.Add(floor);
            return true;
        }
        if (operation != "aura-move")
            return false;
        if (Player.m_localPlayer == null || Player.m_localPlayer.IsTeleporting())
            throw new InvalidOperationException("Movement fixture is not settled");
        if (auraController == null)
        {
            auraController = this;
            safetyHarmony.Patch(
                AccessTools.Method(typeof(PlayerController), "FixedUpdate"),
                prefix: new HarmonyMethod(typeof(QaGameplay), nameof(AuraControllerInput))
            );
            safetyHarmony.Patch(
                AccessTools.Method(typeof(SEMan), "ApplyStatusEffectSpeedMods"),
                prefix: new HarmonyMethod(typeof(QaGameplay), nameof(AuraSpeedBefore)),
                postfix: new HarmonyMethod(typeof(QaGameplay), nameof(AuraSpeedAfter))
            );
        }
        Player.m_localPlayer.SetLookDir(Vector3.forward, 0f);
        auraSpeeds.Clear();
        auraTargets.Clear();
        auraBases.Clear();
        auraTimes.Clear();
        auraUnexpectedContacts.Clear();
        auraFreshness.Clear();
        auraNativeBase = auraNativeTarget = 0f;
        auraNativeTick = -1f;
        auraNativeCalls = 0;
        auraMoveStart = Time.realtimeSinceStartup;
        auraMoving = true;
        busy = true;
        StartCoroutine(AuraMotionSample());
        return true;
    }
    private static bool AuraControllerInput(PlayerController __instance)
    {
        if (auraController == null || !auraController.auraMoving || Player.m_localPlayer == null
        || __instance.GetComponent<Player>() != Player.m_localPlayer)
            return true;
        Player.m_localPlayer.SetControls(
            Vector3.forward,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false
        );
        return false;
    }
    private static void AuraSpeedBefore(SEMan __instance, float __0, out float __state)
    {
        __state = float.NaN;
        var player = Player.m_localPlayer;
        if (auraController == null || !auraController.auraMoving || player == null || __instance != player.GetSEMan())
            return;
        if (__0 >= 0f && __0 <= player.m_speed * (1f + player.GetEquipmentMovementModifier()) + .0001f)
            __state = __0;
    }
    private static void AuraSpeedAfter(float __0, float __state)
    {
        if (float.IsNaN(__state) || auraController == null || !auraController.auraMoving)
            return;
        auraController.auraNativeBase = __state;
        auraController.auraNativeTarget = __0;
        auraController.auraNativeTick = Time.fixedTime;
        auraController.auraNativeCalls++;
    }
    private IEnumerator AuraMotionSample()
    {
        Vector3 start = Player.m_localPlayer.transform.position;
        while (Time.realtimeSinceStartup - auraMoveStart < 4f)
        {
            yield return new WaitForFixedUpdate();
            float elapsed = Time.realtimeSinceStartup - auraMoveStart;
            if (elapsed >= 4f)
                break;
            if (elapsed < 2f)
                continue;
            var velocity = Player.m_localPlayer.GetVelocity();
            velocity.y = 0f;
            auraSpeeds.Add(velocity.magnitude);
            auraTargets.Add(auraNativeTarget);
            auraBases.Add(auraNativeBase);
            auraTimes.Add(elapsed);
            bool fresh = Mathf.Abs(auraNativeTick - Time.fixedTime) < Time.fixedDeltaTime * .5f;
            auraFreshness.Add(fresh);
            if (Mathf.Abs(auraNativeTarget - velocity.magnitude) > .010001f)
            {
                var method = AccessTools.Method(typeof(Character), "GetLastGroundCollider");
                var ground = method?.Invoke(Player.m_localPlayer, null) as Collider;
                auraUnexpectedContacts.Add(new JObject
                {
                    ["time"] = elapsed,
                    ["velocity"] = Point(velocity),
                    ["ground"] = ground?.name,
                    ["fresh"] = fresh,
                    ["base"] = auraNativeBase,
                    ["target"] = auraNativeTarget,
                    ["attack"] = Player.m_localPlayer.InAttack(),
                    ["dodge"] = Player.m_localPlayer.InDodge()
                });
            }
        }
        auraMoving = false;
        Player.m_localPlayer.SetMoveDir(Vector3.zero);
        Player.m_localPlayer.SetRun(false);
        var speeds = auraSpeeds.OrderBy(v => v).ToArray();
        sample =
            new JObject
            {
                ["start"] = Point(start),
                ["end"] = Point(Player.m_localPlayer.transform.position),
                ["speeds"] = new JArray(auraSpeeds),
                ["median"] = speeds.Length == 0 ? 0f : speeds[speeds.Length / 2],
                ["nativeTargets"] = new JArray(auraTargets),
                ["nativeBases"] = new JArray(auraBases),
                ["sampleTimes"] = new JArray(auraTimes),
                ["window"] = "2 <= elapsed < 4 seconds, bounded after the native physics yield",
                ["nativeCalls"] = auraNativeCalls,
                ["elapsed"] = Time.realtimeSinceStartup - auraMoveStart,
                ["onGround"] = Player.m_localPlayer.IsOnGround(),
                ["nativeBaseSpeed"] = Player.m_localPlayer.m_speed,
                ["nativeEquipmentMovementModifier"] = Player.m_localPlayer.GetEquipmentMovementModifier(),
                ["floorFriction"] = auraFloorMaterial != null
                    ? (JToken)auraFloorMaterial.dynamicFriction
                    : JValue.CreateNull(),
                ["input"] =
                "QA native SetControls forward/walk; read-only native SEMan input/output; physical UI input not claimed"
            };
        sample["unexpectedContacts"] = auraUnexpectedContacts.DeepClone();
        sample["freshNativeCalls"] = auraFreshness.DeepClone();
        busy = false;
    }
    private void StopAuraMotion()
    {
        if (auraController == this)
            auraController = null;
        if (auraFloorMaterial != null)
            UnityEngine.Object.Destroy(auraFloorMaterial);
    }
}
