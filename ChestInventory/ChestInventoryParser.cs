using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using PhotonPackageParser;

namespace ChestInventory
{
    public class ByteArrayComparer : IEqualityComparer<byte[]>
    {
        public bool Equals(byte[] left, byte[] right)
        {
            if (left == null || right == null)
            {
                return left == right;
            }

            return left.SequenceEqual(right);
        }

        public int GetHashCode(byte[] key)
        {
            if (key == null)
                throw new ArgumentNullException("key");
            return key.Sum(b => b);
        }
    }

    public class Zone
    {
        public string name;
        public string player;
    }

    public class Building
    {
        public string type;
        public float x;
        public float y;
        public Chest chest;
    }

    public class Chest
    {
        public string name;
        public long updated;

        public IList<Tab> tabs;
    }

    public class Tab
    {
        public string name;
        public IList<int> items;
    }

    public class Item
    {
        public int itemId;
        public int count;
        public string itemType;
        public int quality;
        public long value;
        public string creator;
        public long durability;
    }

    public class ChestInventoryParser : PhotonParser
    {
        private ISet<EventCodes> events = new HashSet<EventCodes>();
        private ISet<OperationCodes> ops = new HashSet<OperationCodes>();
        private StringWriter capture;
        private string clientID;
        
        public ChestInventoryParser()
        {
            var timer = new Timer(CheckLogout, null, 10000, 10000);
            clientID = System.IO.File.ReadAllText("client_id.txt");

            ops.Add(OperationCodes.Join);
            // Sent when a particular tab is opened
            ops.Add(OperationCodes.ContainerOpen);
            ops.Add(OperationCodes.InventorySort);
            ops.Add(OperationCodes.InventoryStack);
            ops.Add(OperationCodes.InventoryDestroyItem);
            ops.Add(OperationCodes.InventoryMoveItem);
            ops.Add(OperationCodes.InventoryRecoverItem);
            ops.Add(OperationCodes.InventoryRecoverAllItems);
            ops.Add(OperationCodes.InventorySplitStack);
            ops.Add(OperationCodes.RegisterToObject);
            ops.Add(OperationCodes.UnRegisterFromObject);
            ops.Add(OperationCodes.GetGameServerByCluster);
            ops.Add(OperationCodes.ChangeCluster);
            ops.Add(OperationCodes.SelectCharacter);

            events.Add(EventCodes.TimeSync);
            events.Add(EventCodes.NewBuilding);
            events.Add(EventCodes.BaseVaultInfo);
            events.Add(EventCodes.GuildVaultInfo);
            events.Add(EventCodes.BankVaultInfo);
            events.Add(EventCodes.AttachItemContainer);
            events.Add(EventCodes.DetachItemContainer);
            events.Add(EventCodes.NewSimpleItem);
            events.Add(EventCodes.NewEquipmentItem);
            events.Add(EventCodes.NewFurnitureItem);
            events.Add(EventCodes.NewJournalItem);
            events.Add(EventCodes.NewLaborerItem);
            events.Add(EventCodes.InventoryDeleteItem);
            events.Add(EventCodes.InventoryPutItem);
            events.Add(EventCodes.JoinFinished);
        }

        private readonly bool _debug = false;
        private long _lastPacket = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        private void CheckLogout(object state)
        {
            long currentTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (currentTime - _lastPacket > 20000)
            {
                // Must have logged out / crashed or something
                SendAndReset();
            }
        }

        private void SendAndReset()
        {
            if (capture != null)
            {
                var captured = capture.ToString();
                var post = "[" + captured + "]";
                string url = "https://chestupdater.albionroamapp.com/submit?cid=" + clientID;

                var client = new WebClient();
                client.Headers[HttpRequestHeader.ContentType] = "application/json";
                Uri uri = new Uri(url);
                var servicePoint = ServicePointManager.FindServicePoint(uri);
                servicePoint.Expect100Continue = false;
                client.UploadStringAsync(uri, post);

                File.WriteAllText("updates_" + _lastPacket + ".json", post);
                capture = null;
                Console.WriteLine("Sent update");
            }
        }
        
        protected override void OnEvent(byte code, Dictionary<byte, object> parameters)
        {
            _lastPacket = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (code == 1)
            {
                var eventType = (Int16) parameters[252];
                var eventCode = (EventCodes) eventType;
                if (_debug || events.Contains(eventCode))
                {
                    if (eventCode == EventCodes.TimeSync)
                    {
                        // Don't bother reporting these
                        return;
                    }
                    var metadata = new Dictionary<string, object>();
                    metadata["timestamp"] = _lastPacket;
                    metadata["code"] = code;
                    metadata["type"] = "event";
                    metadata["event"] = Enum.GetName(typeof(EventCodes), eventType);
                    ToJson(parameters, metadata);
                }
            }
        }

        protected override void OnRequest(byte code, Dictionary<byte, object> parameters)
        {
            _lastPacket = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var opType = (Int16) parameters[253];
            var operationCode = (OperationCodes) opType;
            if (_debug || ops.Contains(operationCode))
            {
                if (operationCode == OperationCodes.SelectCharacter)
                {
                    SendAndReset();
                }
                if (operationCode == OperationCodes.GetGameServerByCluster)
                {
                    SendAndReset();
                }
                
                var metadata = new Dictionary<string, object>();
                metadata["timestamp"] = _lastPacket;
                metadata["code"] = code;
                metadata["type"] = "request";
                metadata["op"] = Enum.GetName(typeof(OperationCodes), opType);
                ToJson(parameters, metadata);
            }
        }

        protected override void OnResponse(byte code, short returnCode, string debugMessage,
            Dictionary<byte, object> parameters)
        {
            _lastPacket = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var opType = (Int16) parameters[253];
            var operationCode = (OperationCodes) opType;
            if (_debug || ops.Contains(operationCode))
            {
                var metadata = new Dictionary<string, object>();
                metadata["timestamp"] = _lastPacket;
                metadata["code"] = code;
                metadata["type"] = "response";
                metadata["op"] = Enum.GetName(typeof(OperationCodes), opType);
                ToJson(parameters, metadata);
            }
        }

        private void ToJson<T>(Dictionary<T, object> parameters, Dictionary<string, object> metadata)
        {
            if (capture == null)
            {
                capture = new StringWriter();
            }
            else
            {
                capture.Write(",");
            }

            StringWriter w = capture;
            if (_debug)
            {
                w = new StringWriter();
            }
            w.Write("{");
            w.Write("\"metadata\":");
            WriteDict(w, metadata);
            w.Write(",\"data\":");
            WriteDict(w, parameters);
            w.Write("}");
            if (_debug)
            {
                Console.WriteLine(w.ToString());
                capture.Write(w.ToString());
            }
        }

        private void WriteDict<T, V>(StringWriter w, Dictionary<T, V> parameters)
        {
            bool firstParam = true;
            w.Write("{");
            foreach (var kv in parameters)
            {
                if (firstParam)
                {
                    firstParam = false;
                }
                else
                {
                    w.Write(",");
                }

                w.Write("\"" + kv.Key + "\":");
                WriteValue(w, kv.Value);
            }

            w.Write("}");
        }

        public void WriteValue(StringWriter w, object v)
        {
            if (v == null)
            {
                w.Write("null");
                return;
            }

            if (v.GetType().IsArray)
            {
                Array array = v as Array;
                w.Write("[");
                bool firstMember = true;
                foreach (var o in array)
                {
                    if (firstMember)
                    {
                        firstMember = false;
                    }
                    else
                    {
                        w.Write(",");
                    }

                    WriteValue(w, o);
                }

                w.Write("]");
            }
            else if (v is string s)
            {
                w.Write("\"" + s + "\"");
            }
            else if (v is Dictionary<Int32, Int64> d)
            {
                WriteDict(w, d);
            } else if (v is bool b)
            {
                w.Write(b ? "true" : "false");
            }
            else
            {
                w.Write(v);
            }
        }
    }
}