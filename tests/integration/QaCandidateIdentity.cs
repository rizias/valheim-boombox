using System;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

namespace BoomboxQa
{
    public sealed class QaCandidateIdentity
    {
        public const int IncompatibleProtocol = 101;
        public string Version
        {
            get;
        }
        public int Protocol
        {
            get;
        }
        public static QaCandidateIdentity Current => Read(Path.Combine(QaEnvironment.CandidateRoot, "ValheimBoombox.dll"));

        private QaCandidateIdentity(string version, int protocol)
        {
            Version = version;
            Protocol = protocol;
        }

        public static QaCandidateIdentity Read(string path)
        {
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            var metadata = pe.GetMetadataReader();
            foreach (var handle in metadata.TypeDefinitions)
            {
                var type = metadata.GetTypeDefinition(handle);
                if (metadata.GetString(type.Namespace) != "VikingBoombox"
                    || metadata.GetString(type.Name) != "BoomboxNetworkProtocol")
                    continue;
                string? version = null;
                int protocol = 0;
                foreach (var fieldHandle in type.GetFields())
                {
                    var field = metadata.GetFieldDefinition(fieldHandle);
                    var constant = field.GetDefaultValue();
                    if (constant.IsNil)
                        continue;
                    var value = metadata.GetConstant(constant);
                    string name = metadata.GetString(field.Name);
                    if (name == "Version" && value.TypeCode == ConstantTypeCode.String)
                        version = Encoding.Unicode.GetString(metadata.GetBlobBytes(value.Value));
                    if (name == "Protocol" && value.TypeCode == ConstantTypeCode.Int32)
                        protocol = metadata.GetBlobReader(value.Value).ReadInt32();
                }
                if (!string.IsNullOrEmpty(version) && protocol > 0)
                    return new QaCandidateIdentity(version, protocol);
                break;
            }
            throw new InvalidDataException("Candidate DLL has no valid Boombox version/protocol constants");
        }
    }
}
