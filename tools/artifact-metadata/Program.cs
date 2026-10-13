using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;

using var stream = File.OpenRead(args[0]);
using var pe = new PEReader(stream);
var metadata = pe.GetMetadataReader();
var result = new Dictionary<string, string>
{
    ["assemblyVersion"] = metadata.GetAssemblyDefinition().Version.ToString(),
};
foreach (var handle in metadata.GetAssemblyDefinition().GetCustomAttributes())
{
    var attribute = metadata.GetCustomAttribute(handle);
    if (attribute.Constructor.Kind != HandleKind.MemberReference)
        continue;
    var member = metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
    if (member.Parent.Kind != HandleKind.TypeReference)
        continue;
    var type = metadata.GetTypeReference((TypeReferenceHandle)member.Parent);
    var name = metadata.GetString(type.Name);
    var reader = metadata.GetBlobReader(attribute.Value);
    if (reader.ReadUInt16() != 1)
        throw new InvalidDataException("Invalid attribute prolog");
    if (name == "AssemblyInformationalVersionAttribute")
        result["informationalVersion"] = reader.ReadSerializedString()!;
    else if (name == "AssemblyFileVersionAttribute")
        result["fileVersion"] = reader.ReadSerializedString()!;
    else if (name == "AssemblyMetadataAttribute" && reader.ReadSerializedString() == "SourceRevisionId")
        result["sourceRevision"] = reader.ReadSerializedString()!;
}
Console.WriteLine(JsonSerializer.Serialize(result));
