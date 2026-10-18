using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using UnityEngine;
using VikingBoombox;

internal static class BoatVisibilityChecks
{
    private static readonly List<string> checks = new List<string>();
    private static readonly MethodInfo lateUpdate = typeof(BoomboxBoatHelp).GetMethod(
        "LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic
    );
    private static readonly MethodInfo fixedUpdate = typeof(BoomboxBoatHelp).GetMethod(
        "FixedUpdate", BindingFlags.Instance | BindingFlags.NonPublic
    );

    public static int Main(string[] args)
    {
        string failure = null;
        try
        {
            Run();
        }
        catch (Exception error)
        {
            failure = error.GetBaseException().Message;
        }
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            passed = failure == null,
            checks,
            failure,
            builtProductSha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[0]))),
            scope = "Actual BoomboxBoatHelp source executes against instrumented Unity collaborators. No game, native rendering, profile or save access."
        }));
        return failure == null ? 0 : 1;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
        checks.Add(message);
    }

    private static void Run()
    {
        var settings = new BoomboxSettings();
        BoomboxPlugin.Instance = new BoomboxPlugin { Settings = settings };
        var ship = new Ship();
        var item = new object();
        Player.m_localPlayer = new Player { Equipped = item, ControlledShip = ship };
        var deck = new AudioDeck { Item = item };
        var controller = new BoomboxBoatHelp();
        controller.Initialize(deck);

        settings.ShowVisualEffects.Value = false;
        deck.EmittedWaves++;
        lateUpdate.Invoke(controller, null);
        fixedUpdate.Invoke(controller, null);
        Check(BoomboxBoatWave.Created == 0, "Hidden boat visuals create no particle effect");
        Check(ship.Body.Impulses.Count == 1, "Boat assistance still pushes with visuals hidden");

        settings.ShowVisualEffects.Value = true;
        deck.EmittedWaves++;
        lateUpdate.Invoke(controller, null);
        fixedUpdate.Invoke(controller, null);
        Check(BoomboxBoatWave.Created == 1 && BoomboxBoatWave.Last.Active,
            "Enabling visuals presents the boat effect");
        int presentations = BoomboxBoatWave.Last.Presentations;

        settings.ShowVisualEffects.Value = false;
        deck.EmittedWaves++;
        lateUpdate.Invoke(controller, null);
        fixedUpdate.Invoke(controller, null);
        Check(!BoomboxBoatWave.Last.Active && BoomboxBoatWave.Last.ClearCalls == 1,
            "Hiding an existing boat effect clears it without another presentation");
        Check(BoomboxBoatWave.Last.Presentations == presentations && ship.Body.Impulses.Count == 3,
            "Hiding existing particles preserves the next assistance impulse");

        deck.EmittedWaves++;
        lateUpdate.Invoke(controller, null);
        fixedUpdate.Invoke(controller, null);
        settings.ShowVisualEffects.Value = true;
        lateUpdate.Invoke(controller, null);
        Check(BoomboxBoatWave.Created == 1 && ship.Body.Impulses.Count == 4
                && !BoomboxBoatWave.Last.LastPushed,
            "Showing again reuses the effect without replaying hidden music events");
        Check(BoomboxBoatDrag.Steps == 4, "Visual toggles leave fixed-step drag accounting active");
        foreach (var impulse in ship.Body.Impulses)
            Check(Math.Abs(impulse.x - 2.03f) < .00001f && impulse.y == 0f && impulse.z == 0f,
                "Assistance impulse is unchanged across visible and hidden frames");

        Player.m_localPlayer.ControlledShip = null;
        lateUpdate.Invoke(controller, null);
        Check(!BoomboxBoatWave.Last.Active, "Leaving the helm still clears the boat effect");
    }
}

namespace UnityEngine
{
    public sealed class DefaultExecutionOrderAttribute : Attribute
    {
        public DefaultExecutionOrderAttribute(int order) { }
    }

    public class MonoBehaviour { }

    public enum ForceMode { VelocityChange }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }
        public float magnitude => (float)Math.Sqrt(x * x + y * y + z * z);
        public Vector3 normalized => new Vector3(x / magnitude, y / magnitude, z / magnitude);
        public static Vector3 operator *(Vector3 value, float amount) => new Vector3(
            value.x * amount, value.y * amount, value.z * amount
        );
    }

    public sealed class Rigidbody
    {
        public Vector3 linearVelocity = new Vector3(2f, 0f, 0f);
        public readonly List<Vector3> Impulses = new List<Vector3>();
        public void AddForce(Vector3 impulse, ForceMode mode) => Impulses.Add(impulse);
    }
}

public sealed class ZNetView
{
    public bool IsValid() => true;
    public bool IsOwner() => true;
}

public sealed class Ship
{
    public enum Speed { Stop, Full }
    public readonly Rigidbody Body = new Rigidbody();
    private readonly ZNetView view = new ZNetView();
    public Speed GetSpeedSetting() => Speed.Full;
    public T GetComponent<T>() where T : class => view as T ?? Body as T;
}

public sealed class Player
{
    public static Player m_localPlayer;
    public object Equipped;
    public Ship ControlledShip;
    public bool IsDead() => false;
    public Ship GetControlledShip() => ControlledShip;
}

namespace VikingBoombox
{
    public sealed class Flag
    {
        public bool Value = true;
    }

    public sealed class BoomboxSettings
    {
        public bool LocalGameplayEnabled = true;
        public readonly Flag BoatHelp = new Flag();
        public readonly Flag ShowVisualEffects = new Flag();
    }

    public sealed class BoomboxPlugin
    {
        public static BoomboxPlugin Instance;
        public BoomboxSettings Settings;
        public void LogError(string message) => throw new InvalidOperationException(message);
    }

    public sealed class DeckState
    {
        public bool onBack = true;
    }

    public sealed class AudioDeck
    {
        public readonly DeckState State = new DeckState();
        public int EmittedWaves;
        public object Item;
        public object Visual = new object();
    }

    public static class BoomboxEquipment
    {
        public static object Get(Player player) => player.Equipped;
    }

    public static class BoomboxBoatDrag
    {
        public static int Steps;
        public static float Take(Ship ship) => 10f;
        public static void Step(Ship ship)
        {
            if (ship != null)
                Steps++;
        }
    }

    internal sealed class BoomboxBoatWave
    {
        internal static int Created;
        internal static BoomboxBoatWave Last;
        internal bool Active;
        internal bool LastPushed;
        internal int Presentations;
        internal int ClearCalls;
        internal BoomboxBoatWave()
        {
            Created++;
            Last = this;
        }
        internal void Present(AudioDeck deck, Ship ship, bool pushed)
        {
            Active = true;
            LastPushed = pushed;
            Presentations++;
        }
        internal void Clear()
        {
            Active = false;
            ClearCalls++;
        }
        internal void Destroy() => Active = false;
    }

    public static class BoomboxText
    {
        public static string Get(string key) => key;
    }
}
