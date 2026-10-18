using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BoomboxQa
{
    internal static class QaNetworkScenario
    {
        internal static async Task Run(QaLedger ledger, string scenario, bool boundedAura,
            List<QaSupervised> running, string pipeName)
        {
            if (scenario == "auras")
            {
                await QaAuraCases.Prepare(ledger);
                await QaAuraCases.Run(ledger);
            }
            else if (boundedAura)
                await QaAuraCompletion.Run(ledger, scenario);
            else if (scenario == "aura-point3")
                await QaPoint3Cases.Run(ledger);
            else if (scenario == "aura-point3-tail")
                await QaPoint3Cases.RunTail(ledger);
            else if (scenario == "damage")
                await QaDamageCases.RunCurrent(ledger);
            else if (scenario == "visual")
                await QaVisualCases.Run(ledger);
            else if (scenario == "ui-recipe")
            {
                if (QaUiCases.Combo)
                    await QaUiCases.RunCombo(ledger, running, pipeName);
                else
                    await QaUiCases.Run(ledger);
            }
            else if (scenario == "remote-point5")
                await QaRemoteCases.Run(ledger, running, pipeName);
            else if (scenario == "remote-point5-tail")
                await QaRemoteCases.Run(ledger, running, pipeName, true);
            else if (scenario == "config-point4")
                await QaConfigTail.Run(ledger, running, pipeName);
            else if (scenario == "video-probe")
                await QaVideoProbe.Run(ledger);
            else if (scenario == "model03-catalog")
                await QaCatalogCases.Run(ledger);
            else if (scenario == "model03")
                await QaGeometryCases.Run(ledger);
            else if (scenario == "model03-support")
                await QaSupportCases.Run(ledger);
            else if (scenario == "model03-demo")
                await QaVideoDemo.Run(ledger);
            else if (scenario == "audio")
                await QaNativeAudioCases.Run(ledger);
            else if (scenario == "audio-music")
                await QaNativeMusicCases.Run(ledger);
            else if (scenario == "audio-reset")
                await QaAudioResetCases.Run(ledger);
            else if (scenario == "lifecycle-current")
                await QaLifecycleCurrent.Run(ledger, running, pipeName);
            else if (scenario == "lifecycle")
                await QaLifecycleCases.Run(ledger, running);
            else if (scenario == "lifecycle-teleport")
                await QaLifecycleCases.Run(ledger, running, true);
            else if (scenario.StartsWith("perf-", StringComparison.Ordinal))
                await QaPerfCases.Run(ledger, running, scenario);
            else if (scenario == "candidate")
                QaEvidence.CaptureMatchingJoin(ledger);
            else
                await QaReplication.Run(ledger);
        }
    }
}
