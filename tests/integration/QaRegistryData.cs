using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Win32;

namespace BoomboxQa
{
    public sealed class QaRegistryValue
    {
        public string Name
        {
            get;
            set;
        } = "";
        public RegistryValueKind Kind
        {
            get;
            set;
        }
        public JsonElement Value
        {
            get;
            set;
        }
        public object Typed() => Kind switch
        {
            RegistryValueKind.DWord => Value.GetInt32(),
            RegistryValueKind.QWord => Value.GetInt64(),
            RegistryValueKind.Binary or RegistryValueKind.None => Value.GetBytesFromBase64(),
            RegistryValueKind.String or RegistryValueKind.ExpandString => Value.GetString()!,
            RegistryValueKind.MultiString => Value.EnumerateArray().Select(v => v.GetString()!).ToArray(),
            _ => throw new IOException("Unsupported registry value kind")
        };
        public bool Matches(
            QaRegistryValue? other
        ) => other != null && Name == other.Name && Kind == other.Kind && JsonElement.DeepEquals(Value, other.Value);
    }
    public sealed class QaRegistryData
    {
        public const string Subkey = "Software\\IronGate\\Valheim";
        public bool Existed
        {
            get;
            set;
        }
        public List<QaRegistryValue> Values
        {
            get;
            set;
        } = new();
        public static QaRegistryData Capture()
        {
            using var key = Registry.CurrentUser.OpenSubKey(Subkey, false);
            if (key == null)
                throw new IOException(
                    "Expected Valheim key absent in this account; native preference restore is not safe"
                );
            if (key.SubKeyCount != 0)
                throw new IOException("Unexpected nested Valheim registry write-set");
            var data = new QaRegistryData { Existed = true };
            foreach (string name in key.GetValueNames().OrderBy(s => s, StringComparer.Ordinal))
                data.Values.Add(
                    new QaRegistryValue
                    {
                        Name = name,
                        Kind = key.GetValueKind(name),
                        Value = JsonSerializer.SerializeToElement(
                        key.GetValue(name, "", RegistryValueOptions.DoNotExpandEnvironmentNames)
                    )
                    }
                );
            return data;
        }
        public QaRegistryValue? Find(string name) => Values.FirstOrDefault(v => v.Name == name);
        public static string Hash(
            QaRegistryData data
        ) => Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(data))
        );
    }
    public sealed class QaRegistryTransaction
    {
        public string SnapshotSha
        {
            get;
            set;
        } = "";
        public string AccountSid
        {
            get;
            set;
        } = "";
    }
}
