using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;

namespace Client
{
    public enum PacketType : int
    {
        GET_MENU = 1, ORDER = 2, GET_ORDERS = 3, PAY = 4, QUIT = 99
    }

    public class MonAn
    {
        public int ID { get; set; }
        public string TenMon { get; set; }
        public decimal DonGia { get; set; }
    }

    public class OrderDetailDTO
    {
        public int Ban { get; set; }
        public string TenMon { get; set; }
        public int SL { get; set; }
        public decimal ThanhTien { get; set; }
    }

    public class ServerConnection
    {
        private TcpClient _client;
        private NetworkStream _stream;
        private BinaryReader _reader;
        private BinaryWriter _writer;

        public bool IsConnected => _client != null && _client.Connected;

        public bool Connect(string ip, int port)
        {
            try
            {
                if (IsConnected) return true;
                _client = new TcpClient();
                _client.Connect(ip, port);
                _stream = _client.GetStream();
                _reader = new BinaryReader(_stream);
                _writer = new BinaryWriter(_stream);
                return true;
            }
            catch { return false; }
        }

        public void Disconnect()
        {
            try
            {
                if (IsConnected) SendPacket(PacketType.QUIT, null);
                _client?.Close();
            }
            catch { }
        }

        // Hàm gửi nhận gói tin
        private BinaryReader SendPacket(PacketType type, Action<BinaryWriter> writePayloadAction)
        {
            if (!IsConnected) throw new Exception("Chưa kết nối Server");

            try
            {
                // Đóng gói Payload
                using (MemoryStream ms = new MemoryStream())
                using (BinaryWriter tempWriter = new BinaryWriter(ms))
                {
                    tempWriter.Write((int)type);
                    writePayloadAction?.Invoke(tempWriter);
                    byte[] payload = ms.ToArray();

                    // Gửi Header (Length) + Payload
                    _writer.Write(payload.Length);
                    _writer.Write(payload);
                    _writer.Flush();
                }

                // Nhận phản hồi (nếu không phải lệnh QUIT)
                if (type == PacketType.QUIT) return null;

                int respLength = _reader.ReadInt32();
                byte[] respData = _reader.ReadBytes(respLength);
                return new BinaryReader(new MemoryStream(respData));
            }
            catch (Exception ex)
            {
                _client?.Close();
                throw new Exception("Lỗi mạng: " + ex.Message);
            }
        }

        // Các hàm xử lý khi nhận từ server

        public List<MonAn> GetMenu()
        {
            try
            {
                using (BinaryReader resp = SendPacket(PacketType.GET_MENU, null))
                {
                    bool success = resp.ReadBoolean();
                    string json = resp.ReadString();
                    if (success)
                        return JsonConvert.DeserializeObject<List<MonAn>>(json);
                }
            }
            catch { }
            return new List<MonAn>();
        }

        public (bool success, string msg) Order(int ban, int idMon, int sl)
        {
            try
            {
                using (BinaryReader resp = SendPacket(PacketType.ORDER, (bw) => {
                    bw.Write(ban);
                    bw.Write(idMon);
                    bw.Write(sl);
                }))
                {
                    return (resp.ReadBoolean(), resp.ReadString());
                }
            }
            catch (Exception ex) { return (false, ex.Message); }
        }

        public List<OrderDetailDTO> GetOrders()
        {
            try
            {
                using (BinaryReader resp = SendPacket(PacketType.GET_ORDERS, null))
                {
                    if (resp.ReadBoolean())
                    {
                        string json = resp.ReadString();
                        return JsonConvert.DeserializeObject<List<OrderDetailDTO>>(json);
                    }
                }
            }
            catch { }
            return new List<OrderDetailDTO>();
        }

        public (bool success, string msg, decimal total) Pay(int ban)
        {
            try
            {
                using (BinaryReader resp = SendPacket(PacketType.PAY, (bw) => bw.Write(ban)))
                {
                    bool success = resp.ReadBoolean();
                    string msg = resp.ReadString();
                    decimal total = resp.ReadDecimal();
                    return (success, msg, total);
                }
            }
            catch (Exception ex) { return (false, ex.Message, 0); }
        }
    }
}