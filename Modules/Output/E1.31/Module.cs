﻿//=====================================================================
//	OutputPlugin - E1.31 Plugin for Vixen 3.0
//		The original base code was generated by Visual Studio based
//		on the interface specification intrinsic to the Vixen plugin
//		technology. All other comments and code are the work of the
//		author. Some comments are based on the fundamental work
//		gleaned from published works by others in the Vixen community
//		including those of Jonathon Reinhart.
//=====================================================================

//=====================================================================
// Copyright (c) 2010 Joshua 1 Systems Inc. All rights reserved.
// Redistribution and use in source and binary forms, with or without modification, are
// permitted provided that the following conditions are met:
//    1. Redistributions of source code must retain the above copyright notice, this list of
//       conditions and the following disclaimer.
//    2. Redistributions in binary form must reproduce the above copyright notice, this list
//       of conditions and the following disclaimer in the documentation and/or other materials
//       provided with the distribution.
// THIS SOFTWARE IS PROVIDED BY JOSHUA 1 SYSTEMS INC. "AS IS" AND ANY EXPRESS OR IMPLIED
// WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND
// FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL <COPYRIGHT HOLDER> OR
// CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR
// CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
// SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON
// ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING
// NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF
// ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
// The views and conclusions contained in the software and documentation are those of the
// authors and should not be interpreted as representing official policies, either expressed
// or implied, of Joshua 1 Systems Inc.
//=====================================================================

namespace VixenModules.Output.E131
{
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Net;
    using System.Net.NetworkInformation;
    using System.Net.Sockets;
    using System.Text;
    using System.Windows.Forms;
    using Vixen.Commands;
    using Vixen.Module.Output;
    using VixenModules.Output.E131.J1Sys;
    using VixenModules.Output.E131.Model;
    using VixenModules.Output.E131.ViewModels;
    using VixenModules.Output.E131.Views;

    public class Module : OutputModuleInstanceBase
    {
        private int _eventCount;
        private List<NetworkInterface> _networkInterfaces;
        private byte _seqNum; 
        private long _totalTicks;

        public Module()
        {
            Initialize();
        }

        public override bool HasSetup
        {
            get
            {
                return true;
            }
        }

        public override bool Setup()
        {
            var viewModel = new UniverseSetupViewModel(GetDataModel().Universes);
            var view = new UniverseSetupView(viewModel);
            view.ShowDialog();
            return true;
        }

        public override void Start()
        {
            // a single socket to use for unicast (if needed)
            Socket unicastSocket = null;

            // working ipaddress object

            // a sortedlist containing the multicast sockets we've already done
            var nicSockets = new SortedList<string, Socket>();
            
            // initialize plugin wide stats
            _eventCount = 0;
            _totalTicks = 0;

            // initialize sequence # for E1.31 packet (should it be per universe?)
            _seqNum = 0;

            // initialize messageTexts stringbuilder to hold all warnings/errors
            var messageTexts = new StringBuilder();

            var universes = GetDataModel().Universes;

            // check for configured from/to
            if (OutputCount == 0)
            {
                foreach (var uE in universes)
                {
                    uE.IsActive = false;
                }
            }

            // now we need to scan the universeTable
            foreach (var universeEntry in universes)
            {
                IPAddress ipAddress;
                
                // if it's still active we'll look into making a socket for it
                if (universeEntry.IsActive)
                {
                    // if it's unicast it's fairly easy to do
                    if (universeEntry.Unicast != null)
                    {
                        // is this the first unicast universe?
                        if (unicastSocket == null)
                        {
                            // yes - make a new socket to use for ALL unicasts
                            unicastSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                        }

                        // use the common unicastsocket
                        universeEntry.Socket = unicastSocket;

                        // try to parse our ip address
                        if (!IPAddress.TryParse(universeEntry.Unicast, out ipAddress))
                        {
                            // oops - bad ip, fuss and deactivate
                            universeEntry.IsActive = false;
                            universeEntry.Socket = null;
                            messageTexts.AppendLine("Invalid Unicast IP: " + universeEntry.Unicast + " - " + universeEntry.RowUnivToText);
                        }
                        else
                        {
                            // if good, make our destination endpoint
                            universeEntry.DestIpEndPoint = new IPEndPoint(ipAddress, 5568);
                        }
                    }

                    // if it's multicast roll up your sleeves we've got work to do
                    var multicastId = universeEntry.MulticastNicId;
                    if (multicastId != null)
                    {
                        // create an ipaddress object based on multicast universe ip rules
                        var multicastIpAddress =
                            new IPAddress(new byte[] { 239, 255, (byte)(universeEntry.UniverseNumber >> 8), (byte)(universeEntry.UniverseNumber & 0xff) });

                        // create an ipendpoint object based on multicast universe ip/port rules
                        var multicastIpEndPoint = new IPEndPoint(multicastIpAddress, 5568);

                        // first check for multicast id in nictable
                        var multicast = _networkInterfaces.FirstOrDefault(x => x.Id == multicastId);
                        if (multicast == null)
                        {
                            // no - deactivate and scream & yell!!
                            universeEntry.IsActive = false;
                            messageTexts.AppendLine("Invalid Multicast NIC ID: " + multicastId + " - " + universeEntry.RowUnivToText);
                        }
                        else
                        {
                            // yes - let's get a working networkinterface object
                            // have we done this multicast id before?
                            if (nicSockets.ContainsKey(multicastId))
                            {
                                // yes - easy to do - use existing socket
                                universeEntry.Socket = nicSockets[multicastId];

                                // setup destipendpoint based on multicast universe ip rules
                                universeEntry.DestIpEndPoint = multicastIpEndPoint;
                            }                                
                            else if (multicast.OperationalStatus != OperationalStatus.Up)
                            {
                                // is the interface up?
                                // no - deactivate and scream & yell!!
                                universeEntry.IsActive = false;
                                messageTexts.AppendLine(
                                                         "Multicast Interface Down: " + multicast.Name + " - "
                                                         + universeEntry.RowUnivToText);
                            }
                            else
                            {
                                // new interface in 'up' status - let's make a new udp socket
                                universeEntry.Socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

                                // get a working copy of ipproperties
                                var ipProperties = multicast.GetIPProperties();

                                // get a working copy of all unicasts
                                var unicasts = ipProperties.UnicastAddresses;

                                ipAddress = null;

                                foreach (var unicast in unicasts)
                                {
                                    if (unicast.Address.AddressFamily
                                        == AddressFamily.InterNetwork)
                                    {
                                        ipAddress = unicast.Address;
                                    }
                                }

                                if (ipAddress == null)
                                {
                                    messageTexts.AppendLine(
                                                             "No IP On Multicast Interface: " + multicast.Name + " - "
                                                             + universeEntry.InfoToText);
                                }
                                else
                                {
                                    // set the multicastinterface option
                                    universeEntry.Socket.SetSocketOption(
                                                              SocketOptionLevel.IP, 
                                                              SocketOptionName.MulticastInterface, 
                                                              ipAddress.GetAddressBytes());

                                    // set the multicasttimetolive option
                                    universeEntry.Socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, universeEntry.Ttl);

                                    // setup destipendpoint based on multicast universe ip rules
                                    universeEntry.DestIpEndPoint = multicastIpEndPoint;

                                    // add this socket to the socket table for reuse
                                    nicSockets.Add(multicastId, universeEntry.Socket);
                                }
                            }
                        }
                    }

                    // if still active we need to create an empty packet
                    if (universeEntry.IsActive)
                    {
                        var zeroBfr = new byte[universeEntry.Size];
                        var e131Packet = new E131Packet(GetDataModel().SenderId, string.Empty, 0, (ushort)universeEntry.UniverseNumber, zeroBfr, 0, universeEntry.Size);
                        universeEntry.PhyBuffer = e131Packet.PhyBuffer;
                    }
                }
            }

            // any warnings/errors recorded?
            if (messageTexts.Length > 0)
            {
                // should we display them
                if (GetDataModel().DisplayWarnings)
                {
                    // show our warnings/errors
                    J1MsgBox.ShowMsg(
                                     "The following warnings and errors were detected during startup:", 
                                     messageTexts.ToString(), 
                                     "Startup Warnings/Errors", 
                                     MessageBoxButtons.OK, 
                                     MessageBoxIcon.Exclamation);
                }
            }
        }

        public override void Stop()
        {
            // keep track of interface ids we have shutdown
            var idList = new SortedList<string, int>();

            var universes = GetDataModel().Universes;

            // iterate through universetable
            foreach (var universeEntry in universes)
            {
                // assume multicast
                var id = universeEntry.MulticastNicId;

                // if unicast use psuedo id
                if (universeEntry.Unicast != null)
                {
                    id = "unicast";
                }

                // if active
                if (universeEntry.IsActive)
                {
                    // and a usable socket
                    if (universeEntry.Socket != null)
                    {
                        // if not already done
                        if (!idList.ContainsKey(id))
                        {
                            // record it & shut it down
                            idList.Add(id, 1);
                            universeEntry.Socket.Shutdown(SocketShutdown.Both);
                            universeEntry.Socket.Close();
                            universeEntry.Socket.Dispose();
                            universeEntry.Socket = null;
                        }
                    }
                }
            }
        }

        protected override void _SetOutputCount(int outputCount) {}

        protected override void _UpdateState(Command[] outputStates)
        {
            var stopWatch = Stopwatch.StartNew();
            var channelValues = outputStates.ToChannelValuesAsBytes();
            _eventCount++;

            var dataModel = GetDataModel();
            var universes = dataModel.Universes;
            if (universes == null)
            {
                return;
            }

            var eventRepeatCount = dataModel.EventRepeatCount;
            var activeUniverses = universes.Where(x => x.IsActive);
            foreach (var universeEntry in activeUniverses)
            {
                if (eventRepeatCount > 0 && universeEntry.EventRepeatCount-- > 0
                    && E131Packet.CompareSlots(universeEntry.PhyBuffer, channelValues, universeEntry.StartIndex, universeEntry.Size))
                {
                    continue;
                }

                E131Packet.CopySeqNumSlots(
                                           universeEntry.PhyBuffer,
                                           channelValues,
                                           universeEntry.StartIndex,
                                           universeEntry.Size,
                                           _seqNum++);
                universeEntry.Socket.SendTo(universeEntry.PhyBuffer, universeEntry.DestIpEndPoint);
                universeEntry.EventRepeatCount = eventRepeatCount;
                universeEntry.PacketCount++;
            }

            stopWatch.Stop();

            _totalTicks += stopWatch.ElapsedTicks;
        }

        private Data GetDataModel()
        {
            return (Data)ModuleData;
        }

        private void Initialize()
        {
            _networkInterfaces =
                NetworkInterface.GetAllNetworkInterfaces().Where(
                                                                 x =>
                                                                 x.NetworkInterfaceType.CompareTo(NetworkInterfaceType.Tunnel) != 0
                                                                 && x.SupportsMulticast).ToList();
        }
    }
}