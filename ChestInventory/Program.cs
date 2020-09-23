using System;
using System.Collections.Generic;
using PcapDotNet.Core;
using PcapDotNet.Packets;
using PcapDotNet.Packets.IpV4;
using PcapDotNet.Packets.Transport;
using System.Linq;
using System.Threading;
using PhotonPackageParser;

namespace ChestInventory
{
    class Program
    {
        public static void Main(string[] args)
        {
            new Program().Start();
        }

        private readonly PhotonParser photonParser;

        public Program()
        {
            photonParser = new ChestInventoryParser();
        }

        class PacketWorker
        {
            private readonly PacketCommunicator _pc;
            private readonly HandlePacket _packetHandler;

            public PacketWorker(PacketCommunicator pc, HandlePacket packetHandler)
            {
                _pc = pc;
                _packetHandler = packetHandler;
            }

            public void doWork()
            {
                Console.WriteLine("Receiving packets: " + _pc.DataLink.Kind);
                try
                {
                    _pc.ReceivePackets(0, _packetHandler);
                }
                catch
                {
                    Console.WriteLine("Failed to read from: " + _pc.DataLink.Kind);
                }
            }
        }

        private void Start()
        {
            // var device = PacketDeviceSelector.AskForPacketDevice();
            // var device = new OfflinePacketDevice("dump.pcap"); // Your wireshark dump (IT MUST BE *.pcap)
            IList<LivePacketDevice> allDevices = LivePacketDevice.AllLocalMachine;

            if (allDevices.Count == 0)
            {
                throw new Exception("No interfaces found! Make sure WinPcap is installed.");
            }

            foreach (PacketDevice selectedDevice in allDevices.ToList())
            {
                // Open the device
                Thread t = new Thread(() =>
                {
                    using (PacketCommunicator communicator =
                        selectedDevice.Open(65536, // portion of the packet to capture
                            // 65536 guarantees that the whole packet will be captured on all the link layers
                            PacketDeviceOpenAttributes.Promiscuous, // promiscuous mode
                            1000)) // read timeout
                    {
                        // Compile the filter
                        using (BerkeleyPacketFilter filter = communicator.CreateFilter("ip and udp"))
                        {
                            // Set the filter
                            communicator.SetFilter(filter);
                        }

                        Console.WriteLine("Listening on " + selectedDevice.Description + "...");

                        // start the capture
                        communicator.ReceivePackets(0, PacketHandler);
                    }
                });
                t.Start();
            }
        }

        private void PacketHandler(Packet packet)
        {
            IpV4Datagram ip = packet.Ethernet.IpV4;
            UdpDatagram udp = ip.Udp;

            if (udp == null || (udp.SourcePort != 5056 && udp.DestinationPort != 5056
                                                       && udp.SourcePort != 5055 && udp.DestinationPort != 5055))
            {
                return;
            }

            try
            {
                photonParser.ReceivePacket(udp.Payload.ToArray());
            }
            catch (Exception e)
            {
                // Don't crash when we can't parse the packet
                Console.WriteLine(e);
            }
        }
    }
}