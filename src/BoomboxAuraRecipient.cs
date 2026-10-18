using System;
using UnityEngine;

namespace VikingBoombox
{
    public static class BoomboxAuraRecipient
    {
        private static Player recipient;
        private static int appliedFrame = -1;
        private static readonly double[] elapsed = new double[BoomboxAuraMath.RecoveryCount];
        private static readonly double[] ticks = new double[BoomboxAuraMath.RecoveryCount];
        public static double HealthTicks => ticks[0];
        public static double StaminaTicks => ticks[1];
        public static double EitrTicks => ticks[2];

        internal static void Apply(Player player)
        {
            if (player != Player.m_localPlayer || Time.frameCount == appliedFrame)
                return;
            if (recipient != player)
            {
                Clear();
                recipient = player;
            }
            appliedFrame = Time.frameCount;
            var balance = BoomboxAuraNetwork.Balance;
            var aggregate = BoomboxAuraNetwork.For(player);
            if (player.IsDead()
                || player.IsTeleporting()
                || balance == null
                || Time.timeScale <= 0f
                || AudioListener.pause)
            {
                Array.Clear(elapsed, 0, elapsed.Length);
                BoomboxAuraHud.Remove(player);
                return;
            }
            BoomboxAuraHud.Present(player, aggregate, balance);
            for (int index = 0; index < BoomboxAuraMath.RecoveryCount; index++)
            {
                float amount = aggregate.Values[(int)BoomboxAuraMath.RecoveryBonus(index)];
                if (amount <= 0f)
                {
                    elapsed[index] = 0;
                    continue;
                }
                elapsed[index] += Time.unscaledDeltaTime;
                double count = Math.Floor(elapsed[index] / balance.Period[index]);
                elapsed[index] -= count * balance.Period[index];
                if (count <= 0)
                    continue;
                ticks[index] += count;
                if (index == 0)
                    player.Heal(Grant(amount, count, player.GetMaxHealth() - player.GetHealth()), false);
                else if (index == 1)
                    player.AddStamina(Grant(amount, count, player.GetMaxStamina() - player.GetStamina()));
                else if (player.GetMaxEitr() > 0f)
                    player.AddEitr(Grant(amount, count, player.GetMaxEitr() - player.GetEitr()));
            }
        }

        private static float Grant(
            float amount,
            double count,
            float missing
        ) => (float)Math.Min(Math.Max(0f, missing), amount * count);

        public static void Clear()
        {
            BoomboxAuraHud.Clear(recipient);
            recipient = null;
            appliedFrame = -1;
            Array.Clear(elapsed, 0, elapsed.Length);
            Array.Clear(ticks, 0, ticks.Length);
        }
    }
}
