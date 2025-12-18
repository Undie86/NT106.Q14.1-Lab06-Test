using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace ServerConsole
{
    public enum PacketType : int
    {
        GET_MENU = 1,
        ORDER = 2,
        GET_ORDERS = 3,
        PAY = 4,
        QUIT = 99
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

    class Server
    {
        private const int PORT = 8888;
        private static List<MonAn> _menu = new List<MonAn>();
        // Dictionary lưu Order: Key = Số Bàn, Value = List Món
        private static Dictionary<int, List<MonAn>> _orders = new Dictionary<int, List<MonAn>>();
        private static object _lockObj = new object();

        static void Main(string[] args)
        {
            Console.Title = "SERVER NHÀ HÀNG (Port: " + PORT + ")";
            Console.OutputEncoding = Encoding.UTF8;

            LoadMenu();
            StartServer();
        }

        static void LoadMenu()
        {
            // Tạo dữ liệu giả lập (hoặc đọc file menu.txt nếu muốn)
            _menu.Add(new MonAn { ID = 1, TenMon = "Phở Bò", DonGia = 50000 });
            _menu.Add(new MonAn { ID = 2, TenMon = "Cơm Tấm", DonGia = 40000 });
            _menu.Add(new MonAn { ID = 3, TenMon = "Bún Chả", DonGia = 45000 });
            _menu.Add(new MonAn { ID = 4, TenMon = "Trà Đá", DonGia = 5000 });
            _menu.Add(new MonAn { ID = 5, TenMon = "Bánh Mì", DonGia = 20000 });

            Console.WriteLine($"[System] Đã load {_menu.Count} món ăn.");
        }

        static void StartServer()
        {
            try
            {
                TcpListener listener = new TcpListener(IPAddress.Any, PORT);
                listener.Start();
                Console.WriteLine($"[System] Server đang lắng nghe tại 127.0.0.1:{PORT}...");

                while (true)
                {
                    TcpClient client = listener.AcceptTcpClient();
                    Console.WriteLine($"[Connect] Client mới: {client.Client.RemoteEndPoint}");

                    Thread t = new Thread(HandleClient);
                    t.IsBackground = true;
                    t.Start(client);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Lỗi Server: " + ex.Message);
            }
        }

        static void HandleClient(object obj)
        {
            TcpClient client = (TcpClient)obj;
            NetworkStream stream = client.GetStream();
            BinaryReader reader = new BinaryReader(stream);
            BinaryWriter writer = new BinaryWriter(stream);

            try
            {
                while (client.Connected)
                {
                    // 1. ĐỌC HEADER (Độ dài gói tin)
                    int packetLength = reader.ReadInt32();
                    byte[] payload = reader.ReadBytes(packetLength);

                    // 2. XỬ LÝ PAYLOAD
                    using (MemoryStream ms = new MemoryStream(payload))
                    using (BinaryReader payloadReader = new BinaryReader(ms))
                    {
                        int typeInt = payloadReader.ReadInt32();
                        PacketType type = (PacketType)typeInt;

                        Console.WriteLine($"[REQ] {client.Client.RemoteEndPoint} -> {type}");

                        switch (type)
                        {
                            case PacketType.GET_MENU:
                                HandleGetMenu(writer);
                                break;
                            case PacketType.ORDER:
                                HandleOrder(payloadReader, writer);
                                break;
                            case PacketType.GET_ORDERS:
                                HandleGetOrders(writer);
                                break;
                            case PacketType.PAY:
                                HandlePay(payloadReader, writer);
                                break;
                            case PacketType.QUIT:
                                return; 
                        }
                    }
                }
            }
            catch (EndOfStreamException) { }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi xử lý client: {ex.Message}");
            }
            finally
            {
                client.Close();
                Console.WriteLine("[Disconnect] Client đã thoát.");
            }
        }


        static void SendResponse(BinaryWriter writer, bool success, string msg)
        {
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter bw = new BinaryWriter(ms))
            {
                bw.Write(success);
                bw.Write(msg); 
                byte[] data = ms.ToArray();
                writer.Write(data.Length);
                writer.Write(data);
                writer.Flush();
            }
        }

        static void HandleGetMenu(BinaryWriter writer)
        {
            string json = JsonConvert.SerializeObject(_menu);
            SendResponse(writer, true, json);
        }

        static void HandleOrder(BinaryReader reader, BinaryWriter writer)
        {
            int ban = reader.ReadInt32();
            int idMon = reader.ReadInt32();
            int sl = reader.ReadInt32();

            var mon = _menu.FirstOrDefault(m => m.ID == idMon);
            if (mon != null)
            {
                lock (_lockObj)
                {
                    if (!_orders.ContainsKey(ban)) _orders[ban] = new List<MonAn>();
                    for (int i = 0; i < sl; i++) _orders[ban].Add(mon);
                }
                SendResponse(writer, true, $"Bàn {ban} đặt thành công {sl} x {mon.TenMon}");
                Console.WriteLine($"-> Bàn {ban} +{sl} {mon.TenMon}");
            }
            else
            {
                SendResponse(writer, false, "Món không tồn tại");
            }
        }

        static void HandleGetOrders(BinaryWriter writer)
        {
            var listResult = new List<OrderDetailDTO>();
            lock (_lockObj)
            {
                foreach (var kvp in _orders)
                {
                    var nhomMon = kvp.Value.GroupBy(m => m.ID)
                        .Select(g => new OrderDetailDTO
                        {
                            Ban = kvp.Key,
                            TenMon = g.First().TenMon,
                            SL = g.Count(),
                            ThanhTien = g.Sum(x => x.DonGia)
                        });
                    listResult.AddRange(nhomMon);
                }
            }
            string json = JsonConvert.SerializeObject(listResult);
            SendResponse(writer, true, json);
        }

        static void HandlePay(BinaryReader reader, BinaryWriter writer)
        {
            int ban = reader.ReadInt32();
            decimal total = 0;
            bool found = false;

            lock (_lockObj)
            {
                if (_orders.ContainsKey(ban))
                {
                    total = _orders[ban].Sum(m => m.DonGia);
                    _orders.Remove(ban);
                    found = true;
                }
            }

            // Gửi response đặc biệt cho thanh toán: [Bool] [Msg] [Decimal]
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter bw = new BinaryWriter(ms))
            {
                bw.Write(found); // Success
                bw.Write(found ? "Thanh toán thành công" : "Bàn trống");
                bw.Write(total); // Tổng tiền

                byte[] data = ms.ToArray();
                writer.Write(data.Length);
                writer.Write(data);
                writer.Flush();
            }
            if (found) Console.WriteLine($"-> Bàn {ban} đã thanh toán: {total}");
        }
    }
}