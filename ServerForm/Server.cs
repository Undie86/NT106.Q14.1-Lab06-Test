using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace ServerForm
{
    public partial class Server : Form
    {
        private const int PORT = 8888;
        private TcpListener listener;
        private Thread serverThread;

        private List<MonAn> _menu = new List<MonAn>();
        private Dictionary<int, List<MonAn>> _orders = new Dictionary<int, List<MonAn>>();
        private object _lockObj = new object();

        public Server()
        {
            InitializeComponent();
            CheckForIllegalCrossThreadCalls = false; 
        }
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


        private string GetLocalIPAddress()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    // Lấy IPv4 và bỏ qua Localhost 
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                    {
                        return ip.ToString();
                    }
                }
                return "127.0.0.1"; 
            }
            catch
            {
                return "Unknown";
            }
        }

        void LoadMenu()
        {
            _menu.Clear(); 
            string filePath = "menu.txt";

            try
            {
                if (!File.Exists(filePath))
                {
                    string sampleData = "1;Phở Bò Tái;50000\n" +
                                        "2;Cơm Tấm Sườn;40000\n" +
                                        "3;Bún Chả Hà Nội;45000\n" +
                                        "4;Trà Đá;5000\n" +
                                        "5;Bánh Mì Pate;20000";

                    File.WriteAllText(filePath, sampleData, Encoding.UTF8);
                    Log("[System] Không tìm thấy menu.txt, đã tự tạo file mẫu.");
                }
                string[] lines = File.ReadAllLines(filePath, Encoding.UTF8);

                foreach (string line in lines)
                {
                    // Bỏ qua dòng trống
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    // Cắt chuỗi bằng dấu chấm phẩy ;
                    // Định dạng: ID;Tên Món;Giá
                    string[] parts = line.Split(';');

                    if (parts.Length == 3)
                    {
                        // Dùng TryParse để tránh crash nếu file ghi sai định dạng số
                        if (int.TryParse(parts[0], out int id) &&
                            decimal.TryParse(parts[2], out decimal donGia))
                        {
                            _menu.Add(new MonAn
                            {
                                ID = id,
                                TenMon = parts[1].Trim(), // Trim để xóa khoảng trắng thừa đầu đuôi
                                DonGia = donGia
                            });
                        }
                    }
                }

                Log($"[System] Đã load thành công {_menu.Count} món ăn từ file.");
            }
            catch (Exception ex)
            {
                Log($"[Error] Lỗi khi đọc menu: {ex.Message}");
            }
        }
        void StartServer()
        {
            try
            {
                listener = new TcpListener(IPAddress.Any, PORT);
                listener.Start();
                Log($"[System] Server đang lắng nghe tại port {PORT}...");

                while (true)
                {
                    TcpClient client = listener.AcceptTcpClient();
                    Log($"[Connect] Client mới: {client.Client.RemoteEndPoint}");

                    // Tạo luồng xử lý riêng cho từng client
                    Thread t = new Thread(HandleClient);
                    t.IsBackground = true;
                    t.Start(client);
                }
            }
            catch (Exception ex)
            {
                Log("Lỗi Server: " + ex.Message);
            }
        }

        void Log(string msg)
        {
            if (rtbLog.InvokeRequired)
            {
                rtbLog.Invoke(new Action<string>(Log), msg);
                return;
            }

            rtbLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
            rtbLog.ScrollToCaret(); 
        }

        void HandleClient(object obj)
        {
            TcpClient client = (TcpClient)obj;
            NetworkStream stream = client.GetStream();
            BinaryReader reader = new BinaryReader(stream);
            BinaryWriter writer = new BinaryWriter(stream);
            string clientIP = client.Client.RemoteEndPoint.ToString();

            try
            {
                while (client.Connected)
                {
                    // Đọc Header
                    int packetLength = reader.ReadInt32();
                    byte[] payload = reader.ReadBytes(packetLength);

                    // Xử lý Payload
                    using (MemoryStream ms = new MemoryStream(payload))
                    using (BinaryReader payloadReader = new BinaryReader(ms))
                    {
                        int typeInt = payloadReader.ReadInt32();
                        PacketType type = (PacketType)typeInt;

                        Log($"[REQ] {clientIP} -> {type}");

                        switch (type)
                        {
                            case PacketType.GET_MENU:
                                HandleGetMenu(writer);
                                break;
                            case PacketType.ORDER:
                                HandleOrder(payloadReader, writer, clientIP);
                                break;
                            case PacketType.GET_ORDERS:
                                HandleGetOrders(writer, clientIP);
                                break;
                            case PacketType.PAY:
                                HandlePay(payloadReader, writer, clientIP);
                                break;
                            case PacketType.QUIT:
                                return;
                        }
                    }
                }
            }
            catch (EndOfStreamException) { }
            catch (Exception ex) { Log($"Lỗi client {clientIP}: {ex.Message}"); }
            finally
            {
                client.Close();
                Log($"[Disconnect] {clientIP} đã ngắt kết nối.");
            }
        }


        void SendResponse(BinaryWriter writer, bool success, string msg)
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

        void HandleGetMenu(BinaryWriter writer)
        {
            string json = JsonConvert.SerializeObject(_menu);
            SendResponse(writer, true, json);
        }

        void HandleOrder(BinaryReader reader, BinaryWriter writer, string ip)
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
                SendResponse(writer, true, $"OK");
                Log($"-> Bàn {ban} đặt {sl} x {mon.TenMon}");
            }
            else
            {
                SendResponse(writer, false, "Món không tồn tại");
            }
        }

        void HandleGetOrders(BinaryWriter writer, string ip)
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
            Log($"-> Gửi danh sách Order cho Thu ngân ({ip})");
        }

        void HandlePay(BinaryReader reader, BinaryWriter writer, string ip)
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

            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter bw = new BinaryWriter(ms))
            {
                bw.Write(found);
                bw.Write(found ? "Thanh toán thành công" : "Bàn trống");
                bw.Write(total);

                byte[] data = ms.ToArray();
                writer.Write(data.Length);
                writer.Write(data);
                writer.Flush();
            }

            if (found) Log($"-> Bàn {ban} thanh toán: {total:N0} VNĐ");
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            listener?.Stop();
            serverThread?.Abort();
        }

        private void Form1_Load_1(object sender, EventArgs e)
        {
            string myIP = GetLocalIPAddress();
            if (lblIP != null) lblIP.Text = "IP: " + myIP;
            if (lblPort != null) lblPort.Text = "Port: " + PORT;
            LoadMenu();

            serverThread = new Thread(StartServer);
            serverThread.IsBackground = true;
            serverThread.Start();
        }
    }
}