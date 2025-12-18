using System;
using System.Collections.Generic;
using System.Threading.Tasks; // Cần thiết để dùng Task.Run
using System.Windows.Forms;

namespace Client
{
    public partial class Form1 : Form
    {
        // 1. Khai báo đối tượng ServerConnection
        private ServerConnection server = new ServerConnection();

        public Form1()
        {
            InitializeComponent();
        }

        private async void Form1_Load(object sender, EventArgs e)
        {
            Console.WriteLine("Form đang load...");
            this.Text = "Đang kết nối tới Server...";

            // 2. Kết nối tới Server (Chạy ngầm để không đơ Form)
            bool isConnected = await Task.Run(() => server.Connect("127.0.0.1", 8888));

            if (isConnected)
            {
                this.Text = "ĐÃ KẾT NỐI SERVER";
                MessageBox.Show("Kết nối thành công!");

                // 3. Sau khi kết nối, thử lấy Menu ngay lập tức
                await LoadMenuData();
            }
            else
            {
                this.Text = "MẤT KẾT NỐI";
                MessageBox.Show("Không thể kết nối tới Server!");
            }
        }

        private async Task LoadMenuData()
        {
            try
            {
                List<MonAn> menu = await Task.Run(() => server.GetMenu());

                if (menu != null && menu.Count > 0)
                {
                    string danhSachMon = "Menu hôm nay:\n";
                    foreach (var mon in menu)
                    {
                        danhSachMon += $"{mon.ID}. {mon.TenMon} - {mon.DonGia:N0}đ\n";
                    }
                    MessageBox.Show(danhSachMon, "Dữ liệu từ Server");

                }
                else
                {
                    MessageBox.Show("Không lấy được menu hoặc menu rỗng.");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi khi lấy menu: " + ex.Message);
            }
        }

        private async void btnOrder_Click(object sender, EventArgs e)
        {
            if (!server.IsConnected)
            {
                MessageBox.Show("Chưa kết nối server!");
                return;
            }
            int banID = 1;
            int monID = 2;
            int soLuong = 5;

            var result = await Task.Run(() => server.Order(banID, monID, soLuong));

            if (result.success)
            {
                MessageBox.Show("Đặt món thành công: " + result.msg);
            }
            else
            {
                MessageBox.Show("Thất bại: " + result.msg);
            }
        }

        // Sự kiện khi đóng Form thì ngắt kết nối
        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            server.Disconnect();
        }
    }
}