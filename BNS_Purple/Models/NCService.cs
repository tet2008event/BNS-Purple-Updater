using System;
using System.Collections.Generic;
using System.ComponentModel;
using BNS_Purple.Extensions;
using ProtoBuf;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Linq;
using BNS_Purple.NCServices;

namespace BNS_Purple.Models
{
    public class NCService
    {
        // CDN is useless as I can grab these from the launcher service but still leaving as a backup
        public enum ERegions
        {
            [Description("North America")]
            [RegistryPath("SOFTWARE\\Wow6432Node\\NCWest\\BnS_UE4\\")]
            [GameId("BnS_UE4")]
            [LauncherAddr("updater.nclauncher.ncsoft.com")]
            [AppId("95684EE8-121E-0275-3258-4339DAD72236")]
            [Cligate("cligate.ncsoft.com")]
            [CDN("http://d37ob46rk09il3.cloudfront.net/")]
            [GameIPAddress("18.235.123.165")]
            NA,
            [Description("Europe")]
            [RegistryPath("SOFTWARE\\Wow6432Node\\NCWest\\BnS_UE4\\")]
            [GameId("BnS_UE4")]
            [LauncherAddr("updater.nclauncher.ncsoft.com")]
            [AppId("FBB46F0C-2E6F-FC3C-31E2-6AA5ACDE5DA0")]
            [Cligate("cligate.ncsoft.com")]
            [CDN("http://d37ob46rk09il3.cloudfront.net/")]
            [GameIPAddress("3.75.38.202")]
            EU,
            [Description("Taiwan")]
            [RegistryPath("SOFTWARE\\Wow6432Node\\NCTaiwan\\TWBNS22\\")]
            [GameId("TWBNSUE4")]
            [LauncherAddr("up4svr.plaync.com.tw")]
            [AppId("0D726F91-202E-4158-9B9D-CA0BF9446B36")]
            [Cligate("rccligate.plaync.com.tw")]
            [CDN("http://mmorepo.cdn.plaync.com.tw/")]
            [GameIPAddress("210.242.83.163")]
            TW,
            [Description("Korea")]
            [RegistryPath("SOFTWARE\\Wow6432Node\\plaync\\BNS_LIVE\\")]
            [GameId("BNS_LIVE")]
            [LauncherAddr("up4svr.ncupdate.com")]
            [AppId("0D726F91-202E-4158-9B9D-CA0BF9446B36")]
            [Cligate("cli-purple.g.nc.com")]
            [CDN("http://bnskor.ncupdate.com/")]
            [GameIPAddress("222.122.231.3")]
            KR
        }

        public class PURPLE_FILE_INFO
        {
            public string? versionFormat;
            public string? gameId;
            public string? version;
            public List<PURPLE_FILES_STRUCT> files;
        }

        public class PURPLE_FILES_STRUCT
        {
            public string path;
            public string size;
            public string hash;
            public string patchType;
            public string level;
            public string hashCheck;
            public string version;
            public string entryType;
            public PURPLE_ENCODED_INFO encodedInfo;
            public PURPLE_ENCODED_INFO? deltaInfo;
        }

        public class PURPLE_ENCODED_INFO
        {
            public string path;
            public string size;
            public string hash;
            public List<PURPLE_FILE_ENTRY>? separates;
        }

        public class PURPLE_FILE_ENTRY
        {
            public string path;
            public string size;
            public string hash;
        }

        public enum ServiceCommands
        {
            CompanyInfoRequest,
            ServiceInfoGameListRequest,
            GameInfoLauncherRequest,
            GameInfoUpdateRequest,
            GameInfoExeEnableRequest,
            ServiceInfoDisplayRequest,
            VersionInfoReleaseRequest,
            VersionInfoForwardRequest,
            GameInfoLanguageRequest,
            GameInfoLevelUpdateRequest,
            Max
        }

        private Socket? _Socket;
        private bool _Connected;
        private ERegions _LastRegion;

        protected bool Connect(ERegions regionInfo = 0)
        {
            bool result = false;
            try
            {
                IPAddress ipaddress = Dns.GetHostAddresses(regionInfo.GetAttribute<LauncherAddrAttribute>().Name).FirstOrDefault<IPAddress>();
                if (ipaddress == null)
                {
                    result = false;
                }
                else
                {
                    IPEndPoint remoteEP = new IPEndPoint(ipaddress, 27500);
                    _Socket = new Socket(ipaddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    IAsyncResult asyncResult = _Socket.BeginConnect(remoteEP, null, null);
                    if (!asyncResult.AsyncWaitHandle.WaitOne(3000, false))
                    {
                        throw new SocketException(10060);
                    }
                    _Socket.EndConnect(asyncResult);
                    _Socket.SendTimeout = 5000;
                    _Connected = true;
                    result = true;
                }
            }
            catch (SocketException ex)
            {
                if (_Socket != null)
                {
                    _Connected = false;
                    _Socket.Close();
                    _Socket = null;
                }
            }

            return result;
        }

        private void Disconnect()
        {
            _Connected = false;
            if (_Socket != null)
            {
                try { _Socket.Shutdown(SocketShutdown.Both); }
                catch (Exception)
                {

                }
                finally
                {
                    _Socket.Close();
                    _Socket = null;
                }
            }
        }

        private dynamic SocketSendReceive<T>(byte[] sendBuffer, ERegions regionInfo = 0)
        {
            try
            {
                if (_Socket == null || !_Socket.Connected || _LastRegion != regionInfo)
                {
                    Disconnect();
                    Connect(regionInfo);
                }

                IAsyncResult asyncResult = _Socket.BeginSend(sendBuffer, 0, sendBuffer.Length, SocketFlags.None, null, null);
                if (asyncResult == null)
                {
                    return null;
                }
                else
                {
                    asyncResult.AsyncWaitHandle.WaitOne();
                    _Socket.EndSend(asyncResult);
                    asyncResult.AsyncWaitHandle.Close();
                    byte[] buffer = new byte[4096];
                    IAsyncResult asyncResult2 = _Socket.BeginReceive(buffer, 0, 4096, SocketFlags.None, null, null);
                    if (asyncResult2 == null)
                    {
                        return null;
                    }
                    else
                    {
                        asyncResult2.AsyncWaitHandle.WaitOne();
                        if (_Socket == null)
                        {
                            return null;
                        }
                        else
                        {
                            int length = _Socket.EndReceive(asyncResult2);
                            asyncResult2.AsyncWaitHandle.Close();
                            var pack = Deserialize<T>(buffer, length);

                            if (pack == null)
                                return null;

                            return (T)Convert.ChangeType(pack.Data, typeof(T));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        public GameInfoUpdateAcknowledgement GetGameInfoUpdateRequest(ERegions regionInfo)
        {
            string gameId = regionInfo.GetAttribute<GameIdAttribute>().Name;
            GameInfoUpdateRequest data = new GameInfoUpdateRequest { GameId = gameId };
            PacketPack pack = PacketPack.Factory((ushort)ServiceCommands.GameInfoUpdateRequest, data);
            byte[] buffer = Serialize<GameInfoUpdateRequest>(pack);
            GameInfoUpdateAcknowledgement received = SocketSendReceive<GameInfoUpdateAcknowledgement>(buffer, regionInfo);
            return received;
        }

        public GameInfoLanguageAcknowledgement GetGameInfoLanguageRequest(ERegions regionInfo)
        {
            string gameId = regionInfo.GetAttribute<GameIdAttribute>().Name;
            GameInfoLanguageRequest data = new GameInfoLanguageRequest { GameId = gameId };
            PacketPack pack = PacketPack.Factory((ushort)ServiceCommands.GameInfoLanguageRequest, data);
            byte[] buffer = Serialize<GameInfoLanguageRequest>(pack);
            GameInfoLanguageAcknowledgement received = SocketSendReceive<GameInfoLanguageAcknowledgement>(buffer, regionInfo);
            return received;
        }

        public ServiceInfoDisplayAcknowledgement GetServiceInfoDisplay(ERegions regionInfo)
        {
            string gameId = regionInfo.GetAttribute<GameIdAttribute>().Name;
            ServiceInfoDisplayRequest data = new ServiceInfoDisplayRequest { GameId = gameId, CompanyId = 15 };
            PacketPack pack = PacketPack.Factory((ushort)ServiceCommands.ServiceInfoDisplayRequest, data);
            byte[] buffer = Serialize<ServiceInfoDisplayRequest>(pack);
            ServiceInfoDisplayAcknowledgement received = SocketSendReceive<ServiceInfoDisplayAcknowledgement>(buffer, regionInfo);
            return received;
        }

        public VersionInfoReleaseAcknowledgement GetVersionInfoRelease(ERegions regionInfo)
        {
            string gameId = regionInfo.GetAttribute<GameIdAttribute>().Name;
            VersionInfoReleaseRequest data = new VersionInfoReleaseRequest { GameId = gameId };
            PacketPack pack = PacketPack.Factory((ushort)ServiceCommands.VersionInfoReleaseRequest, data);
            byte[] buffer = Serialize<VersionInfoReleaseRequest>(pack);
            VersionInfoReleaseAcknowledgement received = SocketSendReceive<VersionInfoReleaseAcknowledgement>(buffer, regionInfo);
            return received;
        }

        private byte[] Serialize<T>(PacketPack pack)
        {
            byte[] result = null;
            using (MemoryStream ms = new MemoryStream())
            {
                ms.Position = 4L;
                Serializer.Serialize<T>(ms, (T)Convert.ChangeType(pack.Data, typeof(T)));
                ushort value = (ushort)ms.Position;
                ushort num = (ushort)(ms.Position - 4L);
                ms.Position = 0L;
                byte[] bytes = BitConverter.GetBytes(value);
                ms.Write(bytes, 0, bytes.Length);
                bytes = BitConverter.GetBytes(pack.Command);
                ms.Write(bytes, 0, bytes.Length);
                result = ms.ToArray();
            }
            return result;
        }

        private PacketPack Deserialize<T>(byte[] buffer, int length)
        {
            BitConverter.ToInt16(buffer, 0);
            ushort num = (ushort)BitConverter.ToInt16(buffer, 2);
            if ((ushort)BitConverter.ToInt32(buffer, 4) != 0)
                return null;

            PacketPack packetPack;
            using (MemoryStream ms = new MemoryStream())
            {
                ms.Write(buffer, 8, length - 8);
                ms.Position = 0L;
                packetPack = new PacketPack { Command = num };
                packetPack.Data = Serializer.Deserialize<T>(ms);
            }

            return packetPack;
        }
    }
}
