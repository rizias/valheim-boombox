using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace VikingBoombox
{
    internal static class BoomboxEnchantResources
    {
        private static readonly MethodInfo BigAreaIndex = AccessTools.Method(typeof(MineRock5), "GetAreaIndex");
        private static readonly MethodInfo BigHitArea = AccessTools.Method(typeof(MineRock5), "GetHitArea");
        private static readonly MethodInfo RockAreaIndex = AccessTools.Method(typeof(MineRock), "GetAreaIndex");
        private static readonly FieldInfo DestructibleDestroyed = AccessTools.Field(
            typeof(Destructible),
            "m_destroyed"
        );

        internal static bool IsResource(Component target)
        {
            if (target is MineRock || target is MineRock5 || target is TreeBase || target is TreeLog)
                return true;
            return target is Destructible destructible
            && (destructible.GetDestructibleType() == DestructibleType.Tree
            || destructible.m_damages.m_chop == HitData.DamageModifier.Immune);
        }

        internal static bool IsResource(Collider collider)
        {
            var hit = collider == null ? null : Projectile.FindHitObject(collider);
            if (hit == null)
                return false;
            foreach (var component in hit.GetComponents<MonoBehaviour>())
                if (IsResource(component))
                    return true;
            return false;
        }

        internal static bool Intact(Component target, Collider collider)
        {
            if (target == null)
                return false;
            var view = target.GetComponent<ZNetView>();
            if (view == null || !view.IsValid())
                return false;
            var zdo = view.GetZDO();
            switch (target)
            {
                case MineRock5 big:
                    int bigIndex = collider == null ? -1 : (int)BigAreaIndex.Invoke(big, new object[] { collider });
                    var area = bigIndex < 0 ? null : BigHitArea.Invoke(big, new object[] { bigIndex });
                    return area != null && (float)AccessTools.Field(area.GetType(), "m_health").GetValue(area) > 0f;
                case MineRock rock:
                    int rockIndex = collider == null ? -1 : (int)RockAreaIndex.Invoke(rock, new object[] { collider });
                    return rockIndex >= 0 && zdo.GetFloat("Health" + rockIndex, rock.GetHealth()) > 0f;
                case TreeBase tree:
                    return zdo.GetFloat(ZDOVars.s_health, tree.m_health) > 0f;
                case TreeLog log:
                    return zdo.GetFloat(ZDOVars.s_health, log.m_health) > 0f;
                case Destructible destructible:
                    return !(bool)DestructibleDestroyed.GetValue(destructible)
                        && zdo.GetFloat(ZDOVars.s_health, destructible.m_health) > 0f;
                default:
                    return false;
            }
        }
    }
}
