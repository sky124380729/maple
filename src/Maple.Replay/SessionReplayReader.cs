using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Maple.Replay
{
    [DataContract]
    public sealed class ReplayEvent
    {
        [DataMember(Name = "schemaVersion", IsRequired = true)] public int SchemaVersion { get; set; }
        [DataMember(Name = "timestampMonoMs", IsRequired = true)] public long TimestampMonoMs { get; set; }
        [DataMember(Name = "type", IsRequired = true)] public string Type { get; set; }
        [DataMember(Name = "payloadJson", IsRequired = true)] public string PayloadJson { get; set; }
    }

    public sealed class SessionReplayReader
    {
        public IList<ReplayEvent> Read(TextReader reader)
        {
            if (reader == null) throw new ArgumentNullException("reader");
            var result = new List<ReplayEvent>();
            var serializer = new DataContractJsonSerializer(typeof(ReplayEvent));
            string line;
            long lastTimestamp = long.MinValue;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                ReplayEvent replayEvent;
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(line)))
                {
                    replayEvent = (ReplayEvent)serializer.ReadObject(stream);
                }
                if (replayEvent.SchemaVersion != Maple.Contracts.ContractConstants.SchemaVersion) throw new InvalidDataException("回放 schemaVersion 不兼容");
                if (string.IsNullOrWhiteSpace(replayEvent.Type)) throw new InvalidDataException("回放事件类型为空");
                if (replayEvent.TimestampMonoMs < lastTimestamp) throw new InvalidDataException("回放时间戳必须单调递增");
                lastTimestamp = replayEvent.TimestampMonoMs;
                result.Add(replayEvent);
            }
            return result.AsReadOnly();
        }
    }
}
