using System.Runtime.Serialization;

namespace Maple.Contracts.Preview
{
    [DataContract]
    public sealed class PreviewSurfaceStatus
    {
        [DataMember(Name = "available", IsRequired = true)] public bool Available { get; set; }
        [DataMember(Name = "backend", EmitDefaultValue = false)] public PreviewBackend? Backend { get; set; }
        [DataMember(Name = "reason", EmitDefaultValue = false)] public string Reason { get; set; }
    }

    [DataContract]
    public enum PreviewBackend
    {
        [EnumMember(Value = "native")] Native,
        [EnumMember(Value = "browser-mock")] BrowserMock
    }

    public static class PreviewContractLimits
    {
        public const int MaxPlayers = 64;
        public const int MaxMonsters = 128;
    }
}
