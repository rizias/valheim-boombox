using UnityEngine;

namespace VikingBoombox
{
    internal sealed class BoomboxWeaponCharge
    {
        internal float Value
        {
            get;
            private set;
        }

        internal ItemDrop.ItemData Item
        {
            get;
            private set;
        }

        internal float For(ItemDrop.ItemData held) => held != null && held == Item ? Value : 0f;

        internal void Beat(ItemDrop.ItemData held)
        {
            if (held == null || (held != Item && Value > 0f))
                return;
            Item = held;
            Value = Mathf.Min(1f, Value + BoomboxEnchantCharge.PerBeat);
        }

        internal void Step(ItemDrop.ItemData held, float deltaTime)
        {
            if (Value <= 0f || held == Item)
                return;
            Value = Mathf.Max(0f, Value - BoomboxEnchantCharge.ShieldDecayPerSecond * deltaTime);
            if (Value <= 0f)
                Item = null;
        }

        internal void Spend() => Value = 0f;

        internal void Reset()
        {
            Value = 0f;
            Item = null;
        }
    }

    internal static class BoomboxEnchantWeapons
    {
        internal const float MeleeDamagePerCharge = .20f;
        internal const float StaffEitrPerCharge = .30f;
        internal const float ReloadPerCharge = .30f;
        internal static readonly BoomboxWeaponCharge Melee = new BoomboxWeaponCharge();
        internal static readonly BoomboxWeaponCharge Staff = new BoomboxWeaponCharge();
        internal static readonly BoomboxWeaponCharge Crossbow = new BoomboxWeaponCharge();
        private static Attack armedStaff;
        private static float staffCharge;
        private static float reloadCharge;
        private static bool reloadUsed;
        private static ItemDrop.ItemData swingWeapon;

        internal static float Swing
        {
            get;
            private set;
        }

        internal static bool SwingLanded
        {
            get;
            private set;
        }

        internal static Vector3? PendingBurst
        {
            get;
            set;
        }

        internal static bool IsMelee(ItemDrop.ItemData weapon)
        {
            if (weapon == null || weapon.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Torch)
                return false;
            var skill = weapon.m_shared.m_skillType;
            return skill == Skills.SkillType.Swords
                || skill == Skills.SkillType.Knives
                || skill == Skills.SkillType.Clubs
                || skill == Skills.SkillType.Polearms
                || skill == Skills.SkillType.Spears
                || skill == Skills.SkillType.Axes
                || skill == Skills.SkillType.Unarmed;
        }

        internal static bool IsPickaxe(
            ItemDrop.ItemData weapon
        ) => weapon != null && weapon.m_shared.m_skillType == Skills.SkillType.Pickaxes;

        internal static bool IsStaff(ItemDrop.ItemData weapon) => weapon != null
        && (weapon.m_shared.m_skillType == Skills.SkillType.ElementalMagic
        || weapon.m_shared.m_skillType == Skills.SkillType.BloodMagic);

        internal static bool IsCrossbow(
            ItemDrop.ItemData weapon
        ) => weapon != null && weapon.m_shared.m_skillType == Skills.SkillType.Crossbows;

        internal static void Beat(ItemDrop.ItemData weapon)
        {
            Melee.Beat(IsMelee(weapon) || IsPickaxe(weapon) ? weapon : null);
            Staff.Beat(IsStaff(weapon) ? weapon : null);
            Crossbow.Beat(IsCrossbow(weapon) ? weapon : null);
        }

        internal static void Step(ItemDrop.ItemData weapon, float deltaTime)
        {
            Melee.Step(IsMelee(weapon) || IsPickaxe(weapon) ? weapon : null, deltaTime);
            Staff.Step(IsStaff(weapon) ? weapon : null, deltaTime);
            Crossbow.Step(IsCrossbow(weapon) ? weapon : null, deltaTime);
        }

        internal static float Held(
            ItemDrop.ItemData weapon
        ) => Mathf.Max(Melee.For(weapon), Mathf.Max(Staff.For(weapon), Crossbow.For(weapon)));

        internal static bool Charging(
            ItemDrop.ItemData weapon
        ) => IsMelee(weapon) || IsPickaxe(weapon) || IsStaff(weapon) || IsCrossbow(weapon);

        internal static void Reset()
        {
            Melee.Reset();
            Staff.Reset();
            Crossbow.Reset();
            armedStaff = null;
            staffCharge = 0f;
            reloadCharge = 0f;
            PendingBurst = null;
        }

        internal static void BeginSwing(Humanoid character, ItemDrop.ItemData weapon)
        {
            Swing = character != null && character == Player.m_localPlayer && IsMelee(weapon) ? Melee.For(weapon) : 0f;
            swingWeapon = weapon;
            SwingLanded = false;
        }

        internal static void Landed(HitData hit)
        {
            if (Swing <= 0f
                || (BoomboxGhostStrikes.IsTool(swingWeapon) && BoomboxEnchantResources.IsResource(hit.m_hitCollider)))
                return;
            hit.m_damage.Modify(1f + MeleeDamagePerCharge * Swing);
            if (SwingLanded)
                return;
            SwingLanded = true;
            PendingBurst = hit.m_point;
        }

        internal static void EndSwing()
        {
            if (Swing > 0f && SwingLanded)
                Melee.Spend();
            Swing = 0f;
            SwingLanded = false;
            swingWeapon = null;
        }

        internal static void ArmStaff(Attack attack, Humanoid character, ItemDrop.ItemData weapon)
        {
            bool local = character != null
                && character == Player.m_localPlayer
                && IsStaff(weapon)
                && Staff.For(weapon) > 0f;
            armedStaff = local ? attack : null;
            staffCharge = local ? Staff.For(weapon) : 0f;
        }

        internal static void StaffStarted(Attack attack, bool started)
        {
            if (attack != armedStaff)
                return;
            if (started)
                Staff.Spend();
            else
                armedStaff = null;
        }

        internal static float EitrFactor(Attack attack) => attack != null && attack == armedStaff
            ? 1f - StaffEitrPerCharge * staffCharge
            : 1f;

        internal static void BeginReload(Player player)
        {
            reloadCharge = player != null && player == Player.m_localPlayer
                ? Crossbow.For(player.GetCurrentWeapon())
                : 0f;
            reloadUsed = false;
        }

        internal static float ReloadFactor()
        {
            if (reloadCharge <= 0f)
                return 1f;
            reloadUsed = true;
            return 1f - ReloadPerCharge * reloadCharge;
        }

        internal static void EndReload()
        {
            if (reloadUsed)
                Crossbow.Spend();
            reloadCharge = 0f;
            reloadUsed = false;
        }
    }
}
