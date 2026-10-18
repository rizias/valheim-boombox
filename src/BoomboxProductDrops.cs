using UnityEngine;

namespace VikingBoombox
{
    internal sealed class BoomboxProductDrops
    {
        private readonly string[] products;
        private readonly BoomboxParticleEmitter[] emitters;
        private readonly bool[] chosen;

        private BoomboxProductDrops(string[] products, int count)
        {
            this.products = products;
            emitters = new BoomboxParticleEmitter[count];
            chosen = new bool[count];
        }

        internal static BoomboxProductDrops Build(
            BoomboxParticleVariant variant,
            Transform anchor,
            BoomboxParticleTuning tuning,
            string[] products,
            float interval,
            float lifetime
        )
        {
            int count = products.Length > 1 ? Mathf.FloorToInt(lifetime / interval) + 1 : 1;
            var drops = new BoomboxProductDrops(products, count);
            try
            {
                for (int i = 0; i < count; i++)
                {
                    var own = new BoomboxParticleTuning
                    {
                        Scale = tuning.Scale,
                        Boombox = tuning.Boombox,
                        BurstInterval = interval * count,
                        CoinLifetime = lifetime,
                        Amount = tuning.Amount,
                        Product = drops.Pick()
                    };
                    drops.emitters[i] = BoomboxParticleBuilder.Build(variant, "ProductParticles", anchor, own);
                    drops.chosen[i] = true;
                    if (i > 0)
                        Delay(drops.emitters[i], interval * i);
                }
            }
            catch
            {
                drops.Destroy();
                throw;
            }
            return drops;
        }

        internal void Update()
        {
            for (int i = 0; i < emitters.Length; i++)
            {
                var emitter = emitters[i];
                if (emitter == null)
                    continue;
                emitter.Update();
                if (products.Length < 2 || emitter.Product == null)
                    continue;
                if (emitter.Product.particleCount > 0)
                    chosen[i] = false;
                else if (!chosen[i])
                {
                    BoomboxParticleBuilder.Retarget(emitter, Pick());
                    chosen[i] = true;
                }
            }
        }

        internal void Destroy()
        {
            foreach (var emitter in emitters)
                emitter?.Destroy();
        }

        private string Pick() => products[Random.Range(0, products.Length)];

        private static void Delay(BoomboxParticleEmitter emitter, float delay)
        {
            foreach (Transform layer in emitter.Root.transform)
            {
                var system = layer.GetComponent<ParticleSystem>();
                if (system == null)
                    continue;
                system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = system.main;
                main.startDelay = delay;
                system.Play(true);
            }
        }
    }
}
