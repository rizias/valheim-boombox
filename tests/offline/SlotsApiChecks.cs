using System;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using System.Text.Json;

internal static class SlotsApiChecks
{
    private static int Main(string[] args)
    {
        string productPath = Path.GetFullPath(args[0]), variant = args[3];
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            foreach (string directory in new[] { args[1], args[2] })
            {
                string file = Path.Combine(directory, name.Name + ".dll");
                if (File.Exists(file))
                    return context.LoadFromAssemblyPath(Path.GetFullPath(file));
            }
            return null;
        };
        try
        {
            var product = AssemblyLoadContext.Default.LoadFromAssemblyPath(productPath);
            var equipment = product.GetType("VikingBoombox.BoomboxEquipment", true)!;
            var position = equipment.GetProperty("SlotPosition")!;
            Type vector = Nullable.GetUnderlyingType(position.PropertyType)!;
            Type item = product.GetType(
                "VikingBoombox.BoomboxItemData",
                true
            )!.GetMethod(
                "IsDevice"
            )!.GetParameters()[0].ParameterType;
            Type? api = variant == "absent" ? null : FakeApi(variant, vector, item);
            for (int attempt = 0; attempt < 3; attempt++)
                equipment.GetMethod("Register")!.Invoke(null, null);
            object? slot = position.GetValue(null);
            equipment.GetMethod("IsExtraPosition", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(
                null,
                new[] { Activator.CreateInstance(vector) }
            );
            slot = position.GetValue(null);
            int calls = api == null ? 0 : (int)api.GetField("Calls")!.GetValue(null)!;
            bool working = variant == "working";
            if ((slot != null) != working)
                throw new Exception("Optional API fallback returned an unexpected slot");
            int expectedCalls = working || variant == "throw-add" || variant == "throw-find" || variant == "throw-grid"
                ? 1
                : 0;
            if (calls != expectedCalls)
                throw new Exception("Unsupported API was called repeatedly or before shape validation: " + calls);
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                passed = true,
                variant,
                calls,
                slotAvailable = slot != null,
                productSha = Hash(productPath),
                scope = "Actual product Register and SlotPosition; fake optional API only; no Unity/game process"
            }));
            return 0;
        }
        catch (Exception error)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                passed = false,
                variant,
                error = error.GetBaseException().ToString(),
                productSha = Hash(productPath)
            }));
            return 1;
        }
    }

    private static string Hash(
        string path
    ) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));

    private static Type FakeApi(string variant, Type vector, Type item)
    {
        var module = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("OptionalApiFixture"),
            AssemblyBuilderAccess.Run
        )
            .DefineDynamicModule(
            "api"
        );
        var slot = module.DefineType("ExtraSlots.FixtureSlot", TypeAttributes.Public);
        var constructor = slot.DefineDefaultConstructor(MethodAttributes.Public);
        var getter = slot.DefineMethod(
            "get_GridPosition",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            vector,
            Type.EmptyTypes
        );
        var il = getter.GetILGenerator();
        var value = il.DeclareLocal(vector);
        il.Emit(OpCodes.Ldloca, value);
        il.Emit(OpCodes.Initobj, vector);
        il.Emit(OpCodes.Ldloc, value);
        il.Emit(OpCodes.Ret);
        var property = slot.DefineProperty("GridPosition", PropertyAttributes.None, vector, Type.EmptyTypes);
        property.SetGetMethod(getter);
        Type slotType = slot.CreateType()!;
        var api = module.DefineType(
            "ExtraSlots.API",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed
        );
        var calls = api.DefineField("Calls", typeof(int), FieldAttributes.Public | FieldAttributes.Static);
        if (variant != "missing-add")
        {
            Type result = variant == "wrong-return" ? typeof(int) : typeof(bool);
            var add = api.DefineMethod(
                "AddSlotAfter",
                MethodAttributes.Public | MethodAttributes.Static,
                result,
                new[] {
                typeof(string),
                typeof(Func<string>),
                typeof(Func<,>).MakeGenericType(item, typeof(bool)),
                typeof(Func<bool>),
                typeof(string[])
                }
            );
            il = add.GetILGenerator();
            il.Emit(OpCodes.Ldsfld, calls);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stsfld, calls);
            if (variant == "throw-add")
            {
                il.Emit(OpCodes.Ldstr, "Optional plugin registration failed");
                il.Emit(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor(new[] { typeof(string) })!);
                il.Emit(OpCodes.Throw);
            }
            else
            {
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Ret);
            }
        }
        if (variant != "missing-find")
        {
            var find = api.DefineMethod(
                "FindSlot",
                MethodAttributes.Public | MethodAttributes.Static,
                slotType,
                new[] { typeof(string) }
            );
            il = find.GetILGenerator();
            if (variant == "throw-find")
            {
                il.Emit(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor(Type.EmptyTypes)!);
                il.Emit(OpCodes.Throw);
            }
            else
            {
                il.Emit(OpCodes.Newobj, slotType.GetConstructor(Type.EmptyTypes)!);
                il.Emit(OpCodes.Ret);
            }
        }
        if (variant != "missing-grid")
        {
            var extra = api.DefineMethod(
                "IsGridPositionASlot",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(bool),
                new[] { vector }
            );
            il = extra.GetILGenerator();
            if (variant == "throw-grid")
            {
                il.Emit(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor(Type.EmptyTypes)!);
                il.Emit(OpCodes.Throw);
            }
            else
            {
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Ret);
            }
        }
        return api.CreateType()!;
    }
}
