using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

public static class QaPreloader
{
    public static IEnumerable<string> TargetDLLs => new[] { "assembly_utils.dll" };
    public static void Patch(AssemblyDefinition assembly)
    {
        string root = Environment.GetEnvironmentVariable("BOOMBOX_QA_ROOT");
        string role = Environment.GetEnvironmentVariable("BOOMBOX_QA_ROLE");
        string save = Environment.GetEnvironmentVariable("BOOMBOX_QA_SAVE_ROOT");
        if (role != "server" && role != "client-a" && role != "client-b")
            throw new InvalidOperationException("QA preloader role missing");
        if (Path.GetFullPath(save) != Path.GetFullPath(Path.Combine(root, "profiles", role, "saves")))
            throw new InvalidOperationException("QA preloader savedir mismatch");
        var utils = assembly.MainModule.Types.Single(t => t.Name == "Utils");
        var method = utils.Methods.Single(m => m.Name == "GetSaveDataPath");
        method.Body.Instructions.Clear();
        method.Body.ExceptionHandlers.Clear();
        method.Body.Variables.Clear();
        var il = method.Body.GetILProcessor();
        il.Append(il.Create(OpCodes.Ldstr, "BOOMBOX_QA_SAVE_ROOT"));
        il.Append(
            il.Create(
                OpCodes.Call,
                assembly.MainModule.ImportReference(
                    typeof(Environment).GetMethod("GetEnvironmentVariable", new[] { typeof(string) })
                )
            )
        );
        il.Append(il.Create(OpCodes.Ret));
        var helpers = assembly.MainModule.Types.Single(t => t.Name == "FileHelpers");
        foreach (var cloud in helpers.Methods.Where(
            m => m.Name == "get_CloudStorageSupported" || m.Name == "get_CloudStorageSupportedAndEnabled"
        ))
        {
            cloud.Body.Instructions.Clear();
            cloud.Body.ExceptionHandlers.Clear();
            cloud.Body.Variables.Clear();
            var processor = cloud.Body.GetILProcessor();
            processor.Append(processor.Create(OpCodes.Ldc_I4_0));
            processor.Append(processor.Create(OpCodes.Ret));
        }
    }
}
